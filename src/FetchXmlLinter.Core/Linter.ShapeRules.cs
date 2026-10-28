using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace FetchXmlLinter.Core
{
    public sealed partial class Linter
    {
        private const string Joins = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/join-tables";
        private const string FetchReference = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/reference/fetch";
        private static bool True(XAttribute? attribute) => attribute?.Value == "true" || attribute?.Value == "1";
        private static string LinkType(XElement link) => link.Attribute("link-type")?.Value ?? "inner";

        private static void AnalyzeShape(XElement root, List<XElement> entities, AnalysisResult result, HashSet<string> enabled)
        {
            var links = root.Descendants("link-entity").ToList();
            var orders = root.Descendants("order").ToList();
            var shape = new QueryShape
            {
                ExplicitColumns = root.Descendants("attribute").Count(),
                AllColumnSelections = entities.Count(e => e.Element("all-attributes") != null ||
                    (e.Name == "entity" && !e.Elements("attribute").Any())),
                Joins = links.Count,
                InnerJoins = links.Count(l => LinkType(l) == "inner"),
                OuterJoins = links.Count(l => LinkType(l) == "outer"),
                MaximumJoinDepth = links.Select(l => l.Ancestors("link-entity").Count() + 1).DefaultIfEmpty(0).Max(),
                SortColumns = orders.Count,
                Distinct = True(root.Attribute("distinct"))
            };
            shape.OtherJoins = shape.Joins - shape.InnerJoins - shape.OuterJoins;
            result.Shape = shape;

            void Note(string id, XElement node, string evidence, string explanation, string action, string limitation, string url)
            {
                if (enabled.Contains(id))
                    Add(result, id, node, evidence, explanation, action, limitation, url,
                        FindingCategory.ContextualReview, Severity.Information);
            }
            void Problem(string id, XElement node, string evidence, string explanation, string action, string url,
                Severity severity = Severity.Error)
            {
                if (enabled.Contains(id))
                    Add(result, id, node, evidence, explanation, action,
                        "Static detection only; this tool does not execute or rewrite the query.", url,
                        FindingCategory.Structural, severity);
            }

            if (shape.Joins > 15)
                Problem("FXL009", root, shape.Joins + " link-entity elements; documented maximum 15",
                    "The query exceeds the documented link-entity limit.", "Simplify the query while preserving required relationships.", Joins);
            if (shape.Joins > 1)
                Note("FXL010", root, shape.Joins + " joins: " + shape.InnerJoins + " inner, " + shape.OuterJoins +
                    " outer, " + shape.OtherJoins + " specialized",
                    "Multiple joins increase query complexity, but their cost depends on data and security.",
                    "Review each relationship's purpose, selected columns and filtering before considering hints.",
                    "Two joins is a review trigger, not a Microsoft performance threshold or proof of slowness.", Joins);
            if (shape.MaximumJoinDepth > 1)
                Note("FXL011", root, "Maximum link nesting depth=" + shape.MaximumJoinDepth,
                    "The query contains a chain of joins.",
                    "Review whether the relationship chain expresses the simplest correct requirement.",
                    "Nesting is not inherently wrong; shortening the chain can change results.", Joins);

            foreach (var link in links)
            {
                var alias = link.Attribute("alias")?.Value;
                var referencedElsewhere = alias != null && root.Descendants().Any(n =>
                    string.Equals(n.Attribute("entityname")?.Value, alias, StringComparison.OrdinalIgnoreCase) && !n.Ancestors().Contains(link));
                if (LinkType(link) == "inner" && !link.Elements("attribute").Any() &&
                    !link.Elements("all-attributes").Any() && !link.Descendants("link-entity").Any() &&
                    !link.Descendants("order").Any() && !referencedElsewhere && !True(link.Attribute("intersect")))
                    Note("FXL012", link, "Non-projecting inner join to " + link.Attribute("name")?.Value,
                        "This join may express an existence requirement; it is not assumed to be unused.",
                        "If only existence matters, review whether exists/in would preserve the consumer's required results.",
                        "Changing link type can remove duplicate parent rows and alter results. No conversion is performed.", Joins);
                if (LinkType(link) == "outer" && alias != null && referencedElsewhere)
                    Note("FXL013", link, "Outer alias '" + alias + "' referenced outside its link",
                        "A reference outside an outer join may apply after matching rather than inside the join.",
                        "Review external conditions, null handling and ordering against the intended unmatched-row behavior.",
                        "An anti-join using a null condition is a valid pattern. Outer joins are not automatically unnecessary.", Joins);
            }

            if (enabled.Contains("FXL014"))
            {
                foreach (var filter in root.Descendants("filter").Where(f => f.Attribute("type")?.Value == "or" &&
                    f.Elements().Count(e => e.Name == "condition" || e.Name == "filter") > 1 &&
                    !f.AncestorsAndSelf("filter").Any(fa => fa.Attribute("hint")?.Value == "union")))
                {
                    var contexts = new HashSet<XElement>();
                    foreach (var condition in filter.Descendants("condition").Where(c =>
                        c.Ancestors().FirstOrDefault(IsEntity) == filter.Ancestors().FirstOrDefault(IsEntity)))
                    {
                        var alias = condition.Attribute("entityname")?.Value;
                        var matches = alias == null ? new List<XElement>() :
                            entities.Where(e => string.Equals(e.Attribute("alias")?.Value, alias, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                        var context = alias == null ? condition.Ancestors().FirstOrDefault(IsEntity) :
                            matches.Count == 1 ? matches[0] : null;
                        if (context == null)
                            Incomplete(result, "Cross-table OR alias could not be resolved at " + Location(condition));
                        else contexts.Add(context);
                    }
                    if (contexts.Count > 1)
                        Note("FXL014", filter, contexts.Count + " table contexts referenced by OR conditions",
                            "OR conditions across tables can create a difficult query shape.",
                            "Review simplification or a controlled union-hint experiment, preserving grouping and aliases.",
                            "Union is situational: one OR filter hint per query, at most three filter levels deep. Benefit is unmeasured.", Performance);
                }
            }

            if (shape.Distinct && shape.Joins > 0)
                Note("FXL015", root, "distinct=true with " + shape.Joins + " joins",
                    "Distinct may be intentional, or may compensate for join-driven duplicate rows.",
                    "Review relationship multiplicity before removing distinct or changing joins.",
                    "XML alone cannot establish duplicate counts or whether distinct is unnecessary.", Joins);
            if (shape.Distinct && orders.Count == 0)
                Problem("FXL016", root, "distinct=true; no explicit order",
                    "Distinct queries need explicit ordering for consistent paging.",
                    "Add an appropriate deterministic order for the projected distinct results.", Ordering, Severity.Warning);
            if (orders.Count > 1)
                Note("FXL017", root, orders.Count + " sort columns",
                    "The query requests multiple sort keys.",
                    "Retain required business ordering and unique tie-breakers; review other sort keys.",
                    "A second sort may be essential for stable paging. This is not a universal excessive-sort threshold.", Ordering);
            if (root.Attribute("top") != null)
            {
                var conflicts = new[] { "count", "page", "returntotalrecordcount" }.Where(a => root.Attribute(a) != null).ToList();
                if (conflicts.Count > 0)
                    Problem("FXL018", root, "top combined with " + string.Join(", ", conflicts),
                        "Top is not compatible with paging or returntotalrecordcount attributes.",
                        "Choose bounded top retrieval or the consumer's supported paging/count strategy.", FetchReference);
            }
            foreach (var name in new[] { "top", "count", "page", "aggregatelimit" })
            {
                var value = root.Attribute(name)?.Value;
                if (value != null && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ||
                    number <= 0 || (name == "top" && number > 5000)))
                    Problem("FXL019", root, name + "=" + value,
                        "The numeric limit is not a supported positive integer (top is at most 5,000).",
                        "Use a valid limit appropriate to the execution surface.", FetchReference);
            }
            if (True(root.Attribute("returntotalrecordcount")))
                Note("FXL020", root, "returntotalrecordcount=true",
                    "The query explicitly requests a total record count.",
                    "Confirm the consumer needs the count before changing this setting.",
                    "Model-driven grids may require counts. Presence alone does not establish unnecessary work.", FetchReference);

            var unionFilters = root.Descendants("filter").Where(f => f.Attribute("hint")?.Value == "union").ToList();
            if (unionFilters.Count > 1)
                Problem("FXL022", root, unionFilters.Count + " union hints",
                    "Only one union hint is documented per query.", "Review the OR grouping and keep at most one applicable union hint.", Performance);
            foreach (var filter in unionFilters)
            {
                if (filter.Attribute("type")?.Value != "or")
                    Problem("FXL021", filter, "hint=union; type=" + (filter.Attribute("type")?.Value ?? "and (default)"),
                        "Union requires an OR filter.", "Review the intended boolean logic; do not simply change AND to OR.", Performance);
                var depth = filter.Ancestors("filter").Count() + 1;
                if (depth > 3)
                    Problem("FXL023", filter, "Union filter level=" + depth,
                        "A union hint more than three filter levels deep is documented as ignored.",
                        "Review supported placement without changing filter semantics.", Performance, Severity.Warning);
            }
            var publishedOptions = new HashSet<string>(new[] { "ForceOrder", "DisableRowGoal", "EnableOptimizerHotfixes",
                "LoopJoin", "MergeJoin", "HashJoin", "NO_PERFORMANCE_SPOOL", "ENABLE_HIST_AMENDMENT_FOR_ASC_KEYS" }, StringComparer.Ordinal);
            var unknownOptions = (root.Attribute("options")?.Value ?? "").Split(',').Select(s => s.Trim())
                .Where(s => s.Length > 0 && !publishedOptions.Contains(s)).Distinct().ToList();
            if (unknownOptions.Count > 0)
                Note("FXL029", root, "Not in published options list: " + string.Join(", ", unknownOptions),
                    "These option names are not in the published FetchXML SQL options list.",
                    "Ask Microsoft Support to confirm the exact options and their applicability.",
                    "Absence from documentation does not prove an option is rejected or ignored. All SQL options require support guidance.", Performance);
        }

        private static void AnalyzeProjection(XElement root, IMetadataProvider? metadata, AnalysisResult result, HashSet<string> enabled)
        {
            if (!enabled.Overlaps(new[] { "FXL024", "FXL025", "FXL028" })) return;
            var columns = root.Descendants("attribute").Where(a => a.Attribute("aggregate") == null && a.Attribute("groupby") == null).ToList();
            var logical = new List<string>();
            var calculated = new List<string>();
            var lookups = new List<string>();
            var allLogicalKnown = true;
            var allCalculatedKnown = true;
            var allKindsKnown = true;
            foreach (var column in columns)
            {
                var table = column.Ancestors().FirstOrDefault(IsEntity)?.Attribute("name")?.Value;
                var name = column.Attribute("name")?.Value;
                var field = table == null || name == null ? null : metadata?.FindColumn(table, name);
                var reference = table + "." + name;
                if (field == null)
                {
                    Incomplete(result, "Projection metadata unavailable: " + reference);
                    allLogicalKnown = allCalculatedKnown = allKindsKnown = false;
                    continue;
                }
                if (field.IsLogical == null)
                {
                    allLogicalKnown = false;
                    if (enabled.Contains("FXL024")) Incomplete(result, "Logical classification unavailable: " + reference);
                }
                if (field.IsCalculated == null)
                {
                    allCalculatedKnown = false;
                    if (enabled.Contains("FXL025")) Incomplete(result, "Calculation classification unavailable: " + reference);
                }
                if (field.IsLogical == true) logical.Add(reference);
                if (field.IsCalculated == true) calculated.Add(reference);
                if (field.Kind == ColumnKind.Lookup) lookups.Add(reference);
            }
            result.Shape!.LogicalColumns = allLogicalKnown ? logical.Count : (int?)null;
            result.Shape.CalculatedColumns = allCalculatedKnown ? calculated.Count : (int?)null;
            result.Shape.LookupColumns = allKindsKnown ? lookups.Count : (int?)null;
            foreach (var group in new[] { (Id: "FXL024", Name: "logical", Columns: logical),
                (Id: "FXL025", Name: "calculated/formula", Columns: calculated), (Id: "FXL028", Name: "lookup", Columns: lookups) })
            {
                if (!enabled.Contains(group.Id) || group.Columns.Count == 0) continue;
                Add(result, group.Id, root, group.Columns.Count + " explicitly selected " + group.Name + " columns: " +
                    string.Join(", ", group.Columns), "The projection includes " + group.Name + " columns that may add related-data or computation work.",
                    "Confirm these columns are consumed. Simplify projection before considering a controlled late-materialization experiment.",
                    "There is no universal excessive-column threshold. Required columns must remain; metadata does not establish runtime cost.",
                    AntiPatterns, FindingCategory.ContextualReview, Severity.Information);
            }
        }

        private static void AnalyzeJoinKeys(XElement root, IMetadataProvider? metadata, AnalysisResult result, HashSet<string> enabled)
        {
            if (!enabled.Contains("FXL026") && !enabled.Contains("FXL027")) return;
            foreach (var link in root.Descendants("link-entity"))
            {
                var parent = link.Ancestors().FirstOrDefault(IsEntity);
                var from = link.Attribute("from")?.Value;
                var to = link.Attribute("to")?.Value;
                if (parent == null || from == null || to == null)
                {
                    Incomplete(result, "Implicit join keys cannot be evaluated statically at " + Location(link));
                    continue;
                }
                var table = link.Attribute("name")!.Value;
                var parentTable = parent.Attribute("name")!.Value;
                var childKey = metadata?.FindColumn(table, from);
                var parentKey = metadata?.FindColumn(parentTable, to);
                if (childKey == null || parentKey == null)
                {
                    Incomplete(result, "Join-key metadata unavailable: " + table + "." + from + " -> " + parentTable + "." + to);
                    continue;
                }
                if (enabled.Contains("FXL026"))
                {
                    if (childKey.StorageType == null || parentKey.StorageType == null)
                        Incomplete(result, "Join-key storage type unavailable at " + Location(link));
                    else if (childKey.StorageType != parentKey.StorageType)
                        Add(result, "FXL026", link, table + "." + from + " (" + childKey.StorageType + ") -> " +
                            parentTable + "." + to + " (" + parentKey.StorageType + ")",
                            "The join columns have different metadata type families; cross-type joins are unsupported and may require costly conversion.",
                            "Review compatible join columns and the intended relationship with the table owner.",
                            "Lookup and uniqueidentifier columns are normalized to the same GUID family. No execution plan or conversion cost is measured.",
                            Joins);
                }
                if (enabled.Contains("FXL027") && (LinkType(link) == "inner" || LinkType(link) == "outer"))
                {
                    if (parentKey.IsPrimaryId == null)
                        Incomplete(result, "Parent-key uniqueness classification unavailable at " + Location(link));
                    if (parentKey.IsPrimaryId == true && childKey.Kind == ColumnKind.Lookup && childKey.LookupTargets.Count == 0)
                        Incomplete(result, "Lookup target metadata unavailable at " + Location(link));
                    if (parentKey.IsPrimaryId == true && childKey.Kind == ColumnKind.Lookup &&
                        childKey.LookupTargets.Contains(parentTable, StringComparer.OrdinalIgnoreCase))
                        Add(result, "FXL027", link, table + "." + from + " lookup targets primary key " + parentTable + "." + to,
                            "This relationship direction can return several matching child rows for one parent.",
                            "Review duplicate semantics, projected child columns and whether existence-only retrieval satisfies the requirement.",
                            "Actual fan-out depends on data and filters. Distinct and different link types change results and are not rewritten.",
                            Joins, FindingCategory.ContextualReview, Severity.Information);
                }
            }
        }
    }

}
