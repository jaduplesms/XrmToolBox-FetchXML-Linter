using System;
using System.Linq;
using FetchXmlLinter.Core;
using Xunit;

namespace FetchXmlLinter.Tests;

public class LinterTests
{
    [Fact]
    public void SelectedXmlRulesDoNotEvaluateDisabledMetadataChecks()
    {
        const string xml = "<fetch no-lock='true'><entity name='account'><filter>" +
            "<condition attribute='description' operator='like' value='%text'/></filter></entity></fetch>";
        var result = new Linter().Analyze(xml, new RejectMetadataReads(), enabledRuleIds: new[] { "FXL008", "FXL002" });
        Assert.Equal(AnalysisStatus.Complete, result.Status);
        Assert.Empty(result.NotEvaluated);
        Assert.Equal(new[] { "FXL002", "FXL008" }, result.SelectedRuleIds);
        Assert.Equal(new[] { "FXL002", "FXL008" }, result.Findings.Select(f => f.RuleId));
    }

    [Fact]
    public void MetadataCoverageOnlyReflectsSelectedRulesAndRelevantNodes()
    {
        var metadata = new DictionaryMetadata();
        metadata.Add("account", "name", new ColumnMetadata { Kind = ColumnKind.String });
        const string xml = "<fetch><entity name='account'><attribute name='unavailable'/>" +
            "<filter><condition attribute='name' operator='eq' value='a'/></filter></entity></fetch>";
        var linter = new Linter();
        var largeText = linter.Analyze(xml, metadata, enabledRuleIds: new[] { "FXL004" });
        Assert.Single(largeText.NotEvaluated);
        Assert.Contains("maximum length", largeText.NotEvaluated[0]);
        var calculated = linter.Analyze(xml, metadata, enabledRuleIds: new[] { "FXL003" });
        Assert.Single(calculated.NotEvaluated);
        Assert.Contains("Calculation classification", calculated.NotEvaluated[0]);
    }

    [Fact]
    public void RuleSelectionNeverDisablesXmlSafetyAndStructuralCoverage()
    {
        var linter = new Linter();
        Assert.Equal(AnalysisStatus.Invalid, linter.Analyze(
            "<!DOCTYPE fetch [<!ENTITY x SYSTEM 'file:///never-read'>]><fetch>&x;</fetch>",
            enabledRuleIds: new[] { "FXL008" }).Status);
        var unsupported = linter.Analyze("<fetch><entity name='account'><future/></entity></fetch>",
            enabledRuleIds: new[] { "FXL008" });
        Assert.Equal(AnalysisStatus.Incomplete, unsupported.Status);
        Assert.Contains(unsupported.NotEvaluated, s => s.Contains("future"));
    }

    [Fact]
    public void RuleSelectionRejectsEmptyOrUnknownIdsAndDefaultsToAll()
    {
        var linter = new Linter();
        const string xml = "<fetch><entity name='account'/></fetch>";
        Assert.Throws<ArgumentException>(() => linter.Analyze(xml, enabledRuleIds: Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => linter.Analyze(xml, enabledRuleIds: new[] { "FXL999" }));
        Assert.Equal(RuleCatalog.All.Select(r => r.Id), linter.Analyze(xml).SelectedRuleIds);
    }

    private sealed class RejectMetadataReads : IMetadataProvider
    {
        public ColumnMetadata? FindColumn(string table, string column) =>
            throw new InvalidOperationException("Disabled metadata rules must not run.");
    }

    private static DictionaryMetadata Metadata()
    {
        var metadata = new DictionaryMetadata();
        metadata.Add("account", "name", new ColumnMetadata { Kind = ColumnKind.String, MaxLength = 160, IsCalculated = false, IsLogical = false });
        metadata.Add("account", "description", new ColumnMetadata { Kind = ColumnKind.Memo, IsCalculated = false, IsLogical = false });
        metadata.Add("account", "category", new ColumnMetadata { Kind = ColumnKind.Choice, IsCalculated = false, IsLogical = false });
        metadata.Add("contact", "score", new ColumnMetadata { Kind = ColumnKind.Other, IsCalculated = true, IsLogical = false });
        metadata.Add("contact", "contactid", new ColumnMetadata { StorageType = "Guid", IsPrimaryId = true });
        metadata.Add("account", "primarycontactid", new ColumnMetadata { Kind = ColumnKind.Lookup, StorageType = "Guid", IsPrimaryId = false });
        return metadata;
    }

    [Theory]
    [InlineData("%tail", true)]
    [InlineData("_tail", true)]
    [InlineData("[^a]tail", true)]
    [InlineData("-%tail", true)]
    [InlineData("-tail%", false)]
    [InlineData("[_]tail%", false)]
    [InlineData("prefix%", false)]
    [InlineData("exact", false)]
    public void LeadingWildcardIsOperatorAndEscapeAware(string value, bool expected)
    {
        var xml = "<fetch><entity name='account'><attribute name='name'/><filter>" +
            "<condition attribute='name' operator='like' value='" + value + "'/></filter></entity></fetch>";
        var result = new Linter().Analyze(xml, Metadata());
        Assert.Equal(expected, result.Findings.Any(f => f.RuleId == "FXL002"));
    }

    [Fact]
    public void DoesNotTreatPercentInAnExactMatchAsWildcard()
    {
        var result = new Linter().Analyze("<fetch><entity name='account'><attribute name='name'/><filter>" +
            "<condition attribute='name' operator='eq' value='%literal'/></filter></entity></fetch>", Metadata());
        Assert.DoesNotContain(result.Findings, f => f.RuleId == "FXL002");
    }

    [Fact]
    public void ResolvesConditionEntityAliasToLinkedTable()
    {
        var result = new Linter().Analyze("<fetch><entity name='account'><attribute name='name'/>" +
            "<link-entity name='contact' alias='c' from='contactid' to='primarycontactid'><attribute name='score'/></link-entity>" +
            "<filter><condition entityname='c' attribute='score' operator='gt' value='1'/></filter></entity></fetch>", Metadata());
        Assert.Contains(result.Findings, f => f.RuleId == "FXL003" && f.Evidence.StartsWith("contact.score"));
        Assert.Equal(AnalysisStatus.Complete, result.Status);
    }

    [Fact]
    public void MissingMetadataMeansIncompleteNotHealthy()
    {
        var result = new Linter().Analyze("<fetch><entity name='account'><attribute name='name'/></entity></fetch>");
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.NotEvaluated, s => s.Contains("account.name"));
    }

    [Fact]
    public void MemoProjectionRetainsLayoutWarningAndExactLocation()
    {
        var result = new Linter().Analyze("<fetch>\n<entity name='account'>\n<attribute name='description'/>\n</entity></fetch>",
            Metadata(), "<grid><row><cell name='description'/></row></grid>");
        var finding = Assert.Single(result.Findings, f => f.RuleId == "FXL005");
        Assert.Contains("supplied layout", finding.Evidence);
        Assert.Equal(3, finding.Line);
        Assert.Equal("/fetch[1]/entity[1]/attribute[1]", finding.Location);
    }

    [Theory]
    [InlineData("<!DOCTYPE fetch [<!ENTITY x SYSTEM 'file:///never-read'>]><fetch>&x;</fetch>")]
    [InlineData("<fetch><entity>")]
    [InlineData("<notfetch/>")]
    [InlineData("<fetch><entity name='a'/><entity name='b'/></fetch>")]
    [InlineData("<fetch><entity/></fetch>")]
    public void InvalidOrUnsafeInputFailsExplicitly(string xml)
    {
        var result = new Linter().Analyze(xml);
        Assert.Equal(AnalysisStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void RejectsOversizedInput()
    {
        Assert.Equal(AnalysisStatus.Invalid,
            new Linter().Analyze(new string(' ', Linter.MaximumXmlCharacters + 1)).Status);
    }

    [Fact]
    public void UnknownConstructIsNotSilentlyIgnored()
    {
        var result = new Linter().Analyze("<fetch><entity name='account'><attribute name='name'/><future/></entity></fetch>", Metadata());
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.NotEvaluated, s => s.Contains("future"));
    }

    [Fact]
    public void ChoiceSortAndTuningSettingsHaveCaveats()
    {
        var result = new Linter().Analyze("<fetch options='DisableRowGoal' no-lock='true'><entity name='account'>" +
            "<attribute name='category'/><order attribute='category'/></entity></fetch>", Metadata());
        Assert.Contains(result.Findings, f => f.RuleId == "FXL006" && f.Limitation.Contains("semantics"));
        Assert.Equal(2, result.Findings.Count(f => f.RuleId == "FXL008"));
        Assert.All(result.Findings, f => Assert.NotEmpty(f.DocumentationUrl));
    }

    [Fact]
    public void SameSourceAndMetadataProduceSameFindings()
    {
        const string xml = "<fetch><entity name='account'><attribute name='description'/></entity></fetch>";
        var a = new Linter().Analyze(xml, Metadata());
        var b = new Linter().Analyze(xml, Metadata());
        Assert.Equal(a.SourceHash, b.SourceHash);
        Assert.Equal(a.Findings.Select(f => f.RuleId + f.Location + f.Evidence),
            b.Findings.Select(f => f.RuleId + f.Location + f.Evidence));
    }

    [Theory]
    [InlineData("inner")]
    [InlineData("outer")]
    [InlineData("exists")]
    [InlineData("in")]
    [InlineData("any")]
    [InlineData("not any")]
    [InlineData("all")]
    [InlineData("not all")]
    public void NonProjectingLinksAreNotFlaggedAsAllColumns(string linkType)
    {
        var result = new Linter().Analyze("<fetch><entity name='account'><attribute name='name'/>" +
            "<link-entity name='contact' from='parentcustomerid' to='accountid' link-type='" + linkType +
            "'/></entity></fetch>", Metadata());
        Assert.DoesNotContain(result.Findings, f => f.RuleId == "FXL001");
    }

    [Fact]
    public void AllColumnProjectionDoesNotClaimFullMetadataCoverage()
    {
        var result = new Linter().Analyze("<fetch><entity name='account'/></fetch>", Metadata());
        Assert.Contains(result.Findings, f => f.RuleId == "FXL001");
        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Contains(result.NotEvaluated, s => s.Contains("Unexpanded"));
    }

    [Fact]
    public void LinkedMemoDoesNotBorrowRootLayoutEvidence()
    {
        var metadata = Metadata();
        metadata.Add("contact", "description", new ColumnMetadata { Kind = ColumnKind.Memo });
        var result = new Linter().Analyze("<fetch><entity name='account'><attribute name='name'/>" +
            "<link-entity name='contact' alias='c'><attribute name='description'/></link-entity></entity></fetch>",
            metadata, "<grid><row><cell name='description'/></row></grid>");
        Assert.DoesNotContain("supplied layout", Assert.Single(result.Findings, f => f.RuleId == "FXL005").Evidence);
    }

    [Theory]
    [InlineData("FXL018", "top='5' count='10'", "", true)]
    [InlineData("FXL018", "top='5' returntotalrecordcount='false'", "", true)]
    [InlineData("FXL018", "top='5' page='1'", "", true)]
    [InlineData("FXL018", "count='10' page='2'", "", false)]
    [InlineData("FXL018", "top='5'", "", false)]
    [InlineData("FXL019", "top='5001'", "", true)]
    [InlineData("FXL019", "count='0'", "", true)]
    [InlineData("FXL019", "page='-1'", "", true)]
    [InlineData("FXL019", "aggregatelimit='abc'", "", true)]
    [InlineData("FXL019", "top='5000'", "", false)]
    [InlineData("FXL019", "count='1000' page='2'", "", false)]
    [InlineData("FXL019", "", "", false)]
    [InlineData("FXL020", "returntotalrecordcount='1'", "", true)]
    [InlineData("FXL020", "returntotalrecordcount='false'", "", false)]
    [InlineData("FXL016", "distinct='true'", "", true)]
    [InlineData("FXL016", "distinct='1'", "<order attribute='name'/>", false)]
    [InlineData("FXL016", "", "", false)]
    [InlineData("FXL017", "", "<order attribute='name'/><order attribute='accountid'/>", true)]
    [InlineData("FXL017", "", "<order attribute='name'/>", false)]
    [InlineData("FXL029", "options='OptimizeForUnknown'", "", true)]
    [InlineData("FXL029", "options='ForceOrder, HashJoin'", "", false)]
    public void ExpandedStructuralAndReviewRulesAreSelectable(string id, string attributes, string body, bool expected)
    {
        var result = new Linter().Analyze(Query(attributes, body), new RejectMetadataReads(), enabledRuleIds: new[] { id });
        Assert.Equal(expected, result.Findings.Any(f => f.RuleId == id));
        Assert.All(result.Findings, f => Assert.Equal(id, f.RuleId));
        Assert.Equal(AnalysisStatus.Complete, result.Status);
    }

    private static string Query(string attributes, string body) =>
        "<fetch " + attributes + "><entity name='account'><attribute name='name'/>" + body + "</entity></fetch>";

    [Theory]
    [InlineData(15, false)]
    [InlineData(16, true)]
    public void DocumentedJoinLimitIncludesEveryLink(int count, bool expected)
    {
        var result = new Linter().Analyze(Query("", string.Concat(Enumerable.Repeat(
            "<link-entity name='contact' from='parentcustomerid' to='accountid'/>", count))), enabledRuleIds: new[] { "FXL009" });
        Assert.Equal(expected, result.Findings.Any(f => f.RuleId == "FXL009"));
        Assert.Equal(count, result.Shape!.Joins);
        Assert.All(result.Findings, f => Assert.Equal(Severity.Error, f.Severity));
    }

    [Fact]
    public void QueryShapeReportsCountsAndNestedJoinsWithoutClaimingMeasuredCost()
    {
        var result = new Linter().Analyze(Query("distinct='true'",
            "<order attribute='name'/><order attribute='accountid'/>" +
            "<link-entity name='contact'><attribute name='fullname'/>" +
            "<link-entity name='account' link-type='outer'/></link-entity>" +
            "<link-entity name='contact' link-type='exists'/>"), enabledRuleIds: new[] { "FXL010", "FXL011", "FXL015", "FXL017" });
        Assert.Equal(2, result.Shape!.ExplicitColumns);
        Assert.Equal(3, result.Shape.Joins);
        Assert.Equal(1, result.Shape.InnerJoins);
        Assert.Equal(1, result.Shape.OuterJoins);
        Assert.Equal(1, result.Shape.OtherJoins);
        Assert.Equal(2, result.Shape.MaximumJoinDepth);
        Assert.Equal(2, result.Shape.SortColumns);
        Assert.True(result.Shape.Distinct);
        Assert.Equal(4, result.Findings.Count);
        Assert.All(result.Findings, f => Assert.Equal(Severity.Information, f.Severity));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("<attribute name='fullname'/>", false)]
    [InlineData("<all-attributes/>", false)]
    [InlineData("<order attribute='fullname'/>", false)]
    [InlineData("<link-entity name='account'/>", false)]
    public void ExistenceCandidateDoesNotClaimJoinIsUnused(string body, bool expected)
    {
        var result = new Linter().Analyze(Query("", "<link-entity name='contact' alias='c'>" + body + "</link-entity>"),
            enabledRuleIds: new[] { "FXL012" });
        Assert.Equal(expected, result.Findings.Any(f => f.RuleId == "FXL012" && f.Location == "/fetch[1]/entity[1]/link-entity[1]"));
        Assert.All(result.Findings, f => Assert.Contains("not assumed to be unused", f.Explanation));
    }

    [Fact]
    public void ExternalAliasReferencesSuppressExistenceCandidateAndExplainOuterSemantics()
    {
        foreach (var type in new[] { "inner", "outer" })
        {
            var result = new Linter().Analyze(Query("", "<link-entity name='contact' alias='c' link-type='" + type + "'/>" +
                "<filter><condition entityname='C' attribute='contactid' operator='null'/></filter>"),
                enabledRuleIds: new[] { "FXL012", "FXL013" });
            Assert.DoesNotContain(result.Findings, f => f.RuleId == "FXL012");
            Assert.Equal(type == "outer", result.Findings.Any(f => f.RuleId == "FXL013"));
        }
    }

    [Theory]
    [InlineData("and", false, false)]
    [InlineData("or", false, true)]
    [InlineData("or", true, false)]
    public void CrossTableOrRecognizesAliasContextAndExistingUnion(string type, bool union, bool expected)
    {
        var result = new Linter().Analyze(Query("", "<link-entity name='account' alias='other'/>" +
            "<filter type='" + type + "'" + (union ? " hint='union'" : "") + ">" +
            "<condition attribute='name' operator='eq' value='a'/>" +
            "<condition entityname='other' attribute='name' operator='eq' value='b'/></filter>"),
            enabledRuleIds: new[] { "FXL014" });
        Assert.Equal(expected, result.Findings.Any(f => f.RuleId == "FXL014"));
    }

    [Fact]
    public void SingleBranchOrWrapperDoesNotTurnCrossTableAndIntoAnOrFinding()
    {
        var result = new Linter().Analyze(Query("", "<link-entity name='account' alias='other'/>" +
            "<filter type='or'><filter type='and'><condition attribute='name' operator='eq' value='a'/>" +
            "<condition entityname='other' attribute='name' operator='eq' value='b'/></filter></filter>"),
            enabledRuleIds: new[] { "FXL014" });
        Assert.Empty(result.Findings);
        Assert.Equal(AnalysisStatus.Complete, result.Status);
    }

    [Fact]
    public void UnionValidationSeparatesTypeCountAndIgnoredDepth()
    {
        var invalid = "<filter hint='union'><filter><filter><filter type='or' hint='union'>" +
            "<condition attribute='name' operator='eq' value='a'/></filter></filter></filter></filter>";
        var result = new Linter().Analyze(Query("", invalid), enabledRuleIds: new[] { "FXL021", "FXL022", "FXL023" });
        Assert.Equal(new[] { "FXL022", "FXL021", "FXL023" }, result.Findings.Select(f => f.RuleId));
        var supported = "<filter><filter><filter type='or' hint='union'>" +
            "<condition attribute='name' operator='eq' value='a'/></filter></filter></filter>";
        Assert.Empty(new Linter().Analyze(Query("", supported),
            enabledRuleIds: new[] { "FXL021", "FXL022", "FXL023" }).Findings);
    }

    [Fact]
    public void MetadataProjectionRulesSummarizeColumnsAndPreserveUnknownCoverage()
    {
        var metadata = Metadata();
        metadata.Add("account", "ownerid", new ColumnMetadata { Kind = ColumnKind.Lookup, IsLogical = true, IsCalculated = false });
        metadata.Add("account", "formula", new ColumnMetadata { IsLogical = false, IsCalculated = true });
        var result = new Linter().Analyze(Query("", "<attribute name='ownerid'/><attribute name='formula'/>"), metadata,
            enabledRuleIds: new[] { "FXL024", "FXL025", "FXL028" });
        Assert.Equal(new[] { "FXL024", "FXL025", "FXL028" }, result.Findings.Select(f => f.RuleId));
        Assert.Equal(1, result.Shape!.LookupColumns);
        Assert.Equal(1, result.Shape.LogicalColumns);
        Assert.Equal(1, result.Shape.CalculatedColumns);
        Assert.Equal(AnalysisStatus.Complete, result.Status);
        var missing = new Linter().Analyze(Query("", "<attribute name='missing'/>"), metadata, enabledRuleIds: new[] { "FXL024" });
        Assert.Equal(AnalysisStatus.Incomplete, missing.Status);
        Assert.Null(missing.Shape!.LogicalColumns);
        Assert.Empty(missing.Findings);
    }

    [Fact]
    public void JoinKeysNormalizeGuidLookupsAndOnlyReportPotentialFanOutWithEvidence()
    {
        var metadata = Metadata();
        metadata.Add("account", "accountid", new ColumnMetadata { StorageType = "Guid", IsPrimaryId = true });
        metadata.Add("contact", "parentcustomerid", new ColumnMetadata
        {
            Kind = ColumnKind.Lookup, StorageType = "Guid", LookupTargets = new[] { "account", "contact" }
        });
        var xml = Query("", "<link-entity name='contact' from='parentcustomerid' to='accountid'/>");
        var result = new Linter().Analyze(xml, metadata, enabledRuleIds: new[] { "FXL026", "FXL027" });
        Assert.Equal("FXL027", Assert.Single(result.Findings).RuleId);
        Assert.Equal(AnalysisStatus.Complete, result.Status);
        metadata.Add("contact", "parentcustomerid", new ColumnMetadata { StorageType = "String" });
        result = new Linter().Analyze(xml, metadata, enabledRuleIds: new[] { "FXL026" });
        Assert.Equal("FXL026", Assert.Single(result.Findings).RuleId);
        metadata.Add("contact", "parentcustomerid", new ColumnMetadata { Kind = ColumnKind.Lookup, StorageType = "Guid" });
        var unknownTargets = new Linter().Analyze(xml, metadata, enabledRuleIds: new[] { "FXL027" });
        Assert.Equal(AnalysisStatus.Incomplete, unknownTargets.Status);
        Assert.Empty(unknownTargets.Findings);
        var implicitKeys = new Linter().Analyze(Query("", "<link-entity name='team'/>"), metadata, enabledRuleIds: new[] { "FXL026" });
        Assert.Empty(implicitKeys.Findings);
        Assert.Equal(AnalysisStatus.Incomplete, implicitKeys.Status);
    }

    [Theory]
    [InlineData("true", false)]
    [InlineData("1", false)]
    [InlineData("false", true)]
    public void ChoiceLabelSortCheckRespectsRawOrdering(string raw, bool expected)
    {
        var result = new Linter().Analyze(Query("useraworderby='" + raw + "'", "<order attribute='category'/>"), Metadata(),
            enabledRuleIds: new[] { "FXL006" });
        Assert.Equal(expected, result.Findings.Any(f => f.RuleId == "FXL006"));
    }
}
