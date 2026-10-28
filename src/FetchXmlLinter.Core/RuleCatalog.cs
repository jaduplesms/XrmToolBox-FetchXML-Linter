using System;
using System.Collections.Generic;

namespace FetchXmlLinter.Core
{
    public sealed class RuleDefinition
    {
        internal RuleDefinition(string id, string name, string description, string requirements,
            string limitation, string documentationUrl, bool requiresMetadata = false, string group = "Columns")
        {
            Id = id;
            Name = name;
            Description = description;
            Requirements = requirements;
            Limitation = limitation;
            DocumentationUrl = documentationUrl;
            RequiresMetadata = requiresMetadata;
            Group = group;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Requirements { get; }
        public string Limitation { get; }
        public string DocumentationUrl { get; }
        public bool RequiresMetadata { get; }
        public string Group { get; }
        public override string ToString() => Id + "   " + Group + " | " + Name;
    }

    public static class RuleCatalog
    {
        private const string AntiPatterns = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/query-antipatterns";
        private const string Performance = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/optimize-performance";
        private const string Ordering = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/order-rows";
        private const string Joins = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/join-tables";
        private const string Fetch = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/reference/fetch";

        public static IReadOnlyList<RuleDefinition> All { get; } = Array.AsReadOnly(new[]
        {
            new RuleDefinition("FXL001", "All-column selection",
                "Detects explicit all-column selection and implicit root-table projection.",
                "FetchXML only. Attribute-free links are not assumed to select all columns.",
                "Required display, filter and sort columns must be preserved. Individual all-column checks remain unavailable.", AntiPatterns),
            new RuleDefinition("FXL002", "Leading-wildcard searches",
                "Detects leading wildcards in like filters and suffix matching with ends-with.",
                "FetchXML only. Equality and bracket-escaped wildcard literals are not treated as leading-wildcard searches.",
                "Switching to exact or prefix search changes results. No performance benefit is measured.", AntiPatterns, group: "Filters"),
            new RuleDefinition("FXL003", "Calculated-column filters",
                "Detects filters on metadata-confirmed calculated or formula columns.",
                "Connected column metadata, including calculation classification.",
                "A stored alternative must preserve business semantics and needs review.", AntiPatterns, true, "Filters"),
            new RuleDefinition("FXL004", "Large-text filters",
                "Detects filters on Memo columns or String columns configured over 850 characters.",
                "Connected column type and maximum-length metadata.",
                "Dataverse Search is not a drop-in replacement for FetchXML filtering.", AntiPatterns, true, "Filters"),
            new RuleDefinition("FXL005", "Large-text selection",
                "Identifies explicitly selected Memo columns and any matching supplied layout cells.",
                "Connected column metadata. Layout XML is optional.",
                "Full text may be required by the consumer. Removing it changes displayed information.", Performance, true),
            new RuleDefinition("FXL006", "Choice-column sorting",
                "Identifies sorting on choice columns, which ordinarily uses localized labels.",
                "Connected column metadata. Suppressed when useraworderby is true or 1.",
                "Sorting by integer values instead changes the result order.", Ordering, true, "Sorting"),
            new RuleDefinition("FXL007", "Related-table sorting",
                "Identifies sorting on lookups or linked-table columns.",
                "Connected column metadata and resolvable table aliases.",
                "Changing sorting affects global result order and paging.", Ordering, true, "Sorting"),
            new RuleDefinition("FXL008", "Existing query hints",
                "Inventories options, latematerialize, no-lock and filter hint settings.",
                "FetchXML only. SQL options require Microsoft support guidance.",
                "A setting's presence is not proof of a defect or benefit. Nothing is changed automatically.", Performance, group: "Hints"),
            new RuleDefinition("FXL009", "Documented join limit",
                "Detects more than 15 link-entity elements.", "FetchXML only.",
                "This checks the documented limit, not actual execution cost.", Joins, group: "Joins"),
            new RuleDefinition("FXL010", "Multiple joins",
                "Summarizes two or more joins by link type.", "FetchXML only; review note, not a performance threshold.",
                "Multiple joins may be necessary and efficient.", Joins, group: "Joins"),
            new RuleDefinition("FXL011", "Nested join chains",
                "Reports link nesting beyond one level.", "FetchXML only; review note.",
                "Depth does not prove slowness or justify changing relationships.", Joins, group: "Joins"),
            new RuleDefinition("FXL012", "Existence-only join candidate",
                "Finds non-projecting leaf inner joins without external alias references or sort requirements.",
                "FetchXML only. Intersect links are excluded.",
                "The join is not unused. Replacing it with exists/in changes duplicate semantics.", Joins, group: "Joins"),
            new RuleDefinition("FXL013", "Outer-join external references",
                "Identifies outer aliases referenced outside their join.", "FetchXML only.",
                "Null anti-joins and external ordering can be intentional. Never automatically convert to inner.", Joins, group: "Joins"),
            new RuleDefinition("FXL014", "Cross-table OR",
                "Detects OR filters referencing multiple resolvable table contexts without an existing union hint.",
                "FetchXML and unambiguous aliases.",
                "Union is only a candidate for controlled review; grouping and join semantics must remain correct.", Performance, group: "Filters"),
            new RuleDefinition("FXL015", "Distinct with joins",
                "Flags distinct used with joins for multiplicity review.", "FetchXML only.",
                "Distinct may be required. Actual duplicates and unused joins cannot be proven statically.", Joins, group: "Joins"),
            new RuleDefinition("FXL016", "Distinct without ordering",
                "Detects distinct queries without an explicit order.", "FetchXML only.",
                "Consistent paging requires suitable ordering of distinct results.", Ordering, group: "Sorting"),
            new RuleDefinition("FXL017", "Multiple sort keys",
                "Reports two or more sort keys for review.", "FetchXML only; review trigger, not an excessive-sort threshold.",
                "Unique tie-breakers can be necessary for stable paging.", Ordering, group: "Sorting"),
            new RuleDefinition("FXL018", "Top/paging conflicts",
                "Detects top combined with count, page or returntotalrecordcount.", "FetchXML only.",
                "Choose the strategy appropriate to the consumer; no attribute is removed automatically.", Fetch, group: "Paging"),
            new RuleDefinition("FXL019", "Invalid numeric limits",
                "Validates positive integer top, count, page and aggregatelimit; top cannot exceed 5,000.",
                "FetchXML only.",
                "A valid limit does not prove acceptable row volume. Standard and elastic table page sizes differ.", Fetch, group: "Paging"),
            new RuleDefinition("FXL020", "Requested total count",
                "Reports returntotalrecordcount=true for consumer review.", "FetchXML only.",
                "Model-driven grids may need the count; it is not automatically unnecessary.", Fetch, group: "Paging"),
            new RuleDefinition("FXL021", "Union filter type",
                "Detects union on a non-OR filter.", "FetchXML only.",
                "Changing AND to OR changes results; correct the hint without assuming the intended logic.", Performance, group: "Hints"),
            new RuleDefinition("FXL022", "Multiple union hints",
                "Detects more than one union filter hint.", "FetchXML only.",
                "Only one union hint is documented per query.", Performance, group: "Hints"),
            new RuleDefinition("FXL023", "Ignored deep union hint",
                "Detects union deeper than three filter levels.", "FetchXML only; root filter is level one.",
                "Repositioning filters must preserve boolean and join semantics.", Performance, group: "Hints"),
            new RuleDefinition("FXL024", "Logical-column selection",
                "Summarizes metadata-confirmed logical projections.", "Connected IsLogical metadata for explicit nonaggregate columns.",
                "No universal excessive-column threshold is assumed; required logical values must remain.", AntiPatterns, true),
            new RuleDefinition("FXL025", "Calculated/formula selection",
                "Summarizes explicit calculated/formula projections.", "Connected calculation classification for nonaggregate columns.",
                "Projection cost is not measured; a stored replacement needs business review.", AntiPatterns, true),
            new RuleDefinition("FXL026", "Join-column type compatibility",
                "Compares metadata type families of explicit join keys.", "Connected column type metadata; GUID keys/lookups share one family.",
                "Implicit/inferred keys remain unevaluated. No conversion cost is measured.", Joins, true, "Joins"),
            new RuleDefinition("FXL027", "Potential relationship fan-out",
                "Finds inner/outer joins from a parent primary key to a child lookup targeting that parent table.",
                "Connected primary-key and lookup-target metadata.",
                "Actual fan-out depends on records and filters; neither distinct nor link types are rewritten.", Joins, true, "Joins"),
            new RuleDefinition("FXL028", "Lookup-column selection",
                "Summarizes explicit lookup projections.", "Connected column types for nonaggregate columns.",
                "Lookup columns may be needed. Their count alone does not establish a performance problem.", AntiPatterns, true),
            new RuleDefinition("FXL029", "Unpublished SQL options",
                "Identifies option names absent from the published FetchXML SQL options list.", "FetchXML only; support review.",
                "Absence from documentation does not prove rejection or whether an option is applied. All options require support guidance.",
                Performance, group: "Hints")
        });
    }
}
