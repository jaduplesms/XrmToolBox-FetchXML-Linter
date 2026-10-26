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

    public sealed class AnalysisResult
    {
        public string EngineVersion { get; } = "0.1.0-alpha.1";
        public AnalysisStatus Status { get; set; } = AnalysisStatus.Complete;
        public string SourceHash { get; set; } = "";
        public string Table { get; set; } = "";
        public List<string> Tables { get; } = new List<string>();
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
