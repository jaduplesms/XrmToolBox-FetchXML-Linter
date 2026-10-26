using System.Linq;
using FetchXmlLinter.Core;
using Xunit;

namespace FetchXmlLinter.Tests;

public class LinterTests
{
    private static DictionaryMetadata Metadata()
    {
        var metadata = new DictionaryMetadata();
        metadata.Add("account", "name", new ColumnMetadata { Kind = ColumnKind.String, MaxLength = 160, IsCalculated = false });
        metadata.Add("account", "description", new ColumnMetadata { Kind = ColumnKind.Memo, IsCalculated = false });
        metadata.Add("account", "category", new ColumnMetadata { Kind = ColumnKind.Choice, IsCalculated = false });
        metadata.Add("contact", "score", new ColumnMetadata { Kind = ColumnKind.Other, IsCalculated = true });
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
}
