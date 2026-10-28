using System;
using System.Collections.Generic;

namespace FetchXmlLinter.Core
{
    public enum AnalysisStatus { Complete, Incomplete, Invalid }
    public enum FindingCategory { Structural, DocumentedRisk, ContextualReview }
    public enum Severity { Information, Warning, Error }
    public enum ColumnKind { Other, String, Memo, Choice, Lookup }

    public sealed class ColumnMetadata
    {
        public ColumnKind Kind { get; set; }
        public int? MaxLength { get; set; }
        public bool? IsCalculated { get; set; }
        public bool? IsLogical { get; set; }
        public string? StorageType { get; set; }
        public bool? IsPrimaryId { get; set; }
        public IReadOnlyList<string> LookupTargets { get; set; } = Array.Empty<string>();
    }

    public interface IMetadataProvider
    {
        ColumnMetadata? FindColumn(string table, string column);
    }

    public sealed class Finding
    {
        public string RuleId { get; set; } = "";
        public string RuleVersion { get; set; } = "1";
        public FindingCategory Category { get; set; }
        public Severity Severity { get; set; }
        public string Location { get; set; } = "";
        public int Line { get; set; }
        public string Evidence { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public string Limitation { get; set; } = "";
        public string DocumentationUrl { get; set; } = "";
    }

    public sealed class QueryShape
    {
        public int ExplicitColumns { get; set; }
        public int AllColumnSelections { get; set; }
        public int Joins { get; set; }
        public int InnerJoins { get; set; }
        public int OuterJoins { get; set; }
        public int OtherJoins { get; set; }
        public int MaximumJoinDepth { get; set; }
        public int SortColumns { get; set; }
        public bool Distinct { get; set; }
        public int? LookupColumns { get; set; }
        public int? LogicalColumns { get; set; }
        public int? CalculatedColumns { get; set; }
    }

    public sealed class AnalysisResult
    {
        public string EngineVersion { get; } = "0.1.0-alpha.10";
        public List<string> SelectedRuleIds { get; } = new List<string>();
        public AnalysisStatus Status { get; set; } = AnalysisStatus.Complete;
        public string SourceHash { get; set; } = "";
        public string Table { get; set; } = "";
        public List<string> Tables { get; } = new List<string>();
        public QueryShape? Shape { get; set; }
        public List<Finding> Findings { get; } = new List<Finding>();
        public List<string> NotEvaluated { get; } = new List<string>();
        public string Error { get; set; } = "";
    }

    public sealed class DictionaryMetadata : IMetadataProvider
    {
        private readonly Dictionary<string, ColumnMetadata> columns =
            new Dictionary<string, ColumnMetadata>(StringComparer.OrdinalIgnoreCase);

        public void Add(string table, string column, ColumnMetadata metadata) =>
            columns[table + "." + column] = metadata;

        public ColumnMetadata? FindColumn(string table, string column) =>
            columns.TryGetValue(table + "." + column, out var value) ? value : null;
    }
}
