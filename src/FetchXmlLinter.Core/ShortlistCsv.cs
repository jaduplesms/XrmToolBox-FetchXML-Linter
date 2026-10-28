using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FetchXmlLinter.Core
{
    public sealed class Candidate
    {
        public Guid OrganizationId { get; set; }
        public Guid UserId { get; set; }
        public string UserIdType { get; set; } = "";
        public Guid? ResolvedDataverseUserId { get; set; }
        public Guid? ViewId { get; set; }
        public string Attribution { get; set; } = "";
        public string EntityName { get; set; } = "";
        public DateTimeOffset? WindowStartUtc { get; set; }
        public DateTimeOffset? WindowEndUtc { get; set; }
        public int? SampleCount { get; set; }
        public double? P95Ms { get; set; }
        public string MetricKind { get; set; } = "";
        public override string ToString() =>
            UserIdType + ": " + UserId + " | " + (ViewId?.ToString() ?? "select owned views") + " | " + Attribution;
    }

    public sealed class Shortlist
    {
        public List<Candidate> Candidates { get; } = new List<Candidate>();
        public List<string> Errors { get; } = new List<string>();
        public int DuplicateCount { get; set; }
    }

    public static class ShortlistCsv
    {
        public const string Header = "SchemaVersion,UserId,UserIdType,ViewId,Attribution,EntityName,WindowStartUtc,WindowEndUtc,SampleCount,P95Ms,MetricKind";
        public const int MaximumCharacters = 2 * 1024 * 1024;

        public static Shortlist Read(string text, Guid expectedOrganization)
        {
            var result = new Shortlist();
            if (expectedOrganization == Guid.Empty)
            {
                result.Errors.Add("A connected Dataverse organization ID is required to validate the shortlist.");
                return result;
            }
            if (text.Length > MaximumCharacters)
            {
                result.Errors.Add("CSV exceeds the 2 MiB character limit.");
                return result;
            }
            List<string[]> rows;
            try { rows = Parse(text.TrimStart('\uFEFF')); }
            catch (FormatException ex)
            {
                result.Errors.Add(ex.Message);
                return result;
            }
            if (rows.Count < 2)
            {
                result.Errors.Add("CSV requires a header and at least one candidate.");
                return result;
            }
            var columns = rows[0];
            var required = Header.Split(',');
            var organizationColumn = Array.IndexOf(columns, "OrganizationId");
            if (columns.Length != required.Length + (organizationColumn >= 0 ? 1 : 0) ||
                required.Any(c => columns.Count(h => h == c) != 1))
            {
                result.Errors.Add("CSV must contain the version-one columns exactly once: " + Header +
                    ". OrganizationId is optional; no other columns are supported.");
                return result;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 1; index < rows.Count; index++)
            {
                var cells = rows[index];
                var rowLabel = "CSV row " + (index + 1) + ": ";
                if (cells.Length != columns.Length)
                {
                    result.Errors.Add(rowLabel + "column count does not match the header.");
                    continue;
                }
                string Cell(string name) => cells[Array.IndexOf(columns, name)].Trim();
                try
                {
                    if (Cell("SchemaVersion") != "1") throw new FormatException("unsupported SchemaVersion.");
                    var candidate = new Candidate
                    {
                        OrganizationId = organizationColumn >= 0 ? NonEmptyGuid(Cell("OrganizationId")) : expectedOrganization,
                        UserId = NonEmptyGuid(Cell("UserId")),
                        UserIdType = Cell("UserIdType"),
                        Attribution = Cell("Attribution"),
                        EntityName = Cell("EntityName"),
                        MetricKind = Cell("MetricKind"),
                        ViewId = Cell("ViewId").Length == 0 ? (Guid?)null : NonEmptyGuid(Cell("ViewId"))
                    };
                    if (candidate.OrganizationId != expectedOrganization)
                        throw new FormatException("organization does not match the active Dataverse connection.");
                    if (candidate.UserIdType != "DataverseSystemUserId" && candidate.UserIdType != "EntraObjectId")
                        throw new FormatException("UserIdType must be DataverseSystemUserId or EntraObjectId.");
                    if (candidate.Attribution != "ExactViewMarker" && candidate.Attribution != "UserTableCandidate")
                        throw new FormatException("Attribution must be ExactViewMarker or UserTableCandidate.");
                    if ((candidate.Attribution == "ExactViewMarker") != candidate.ViewId.HasValue)
                        throw new FormatException("only ExactViewMarker rows must contain a ViewId.");
                    if (Cell("WindowStartUtc").Length > 0 || Cell("WindowEndUtc").Length > 0)
                    {
                        candidate.WindowStartUtc = Utc(Cell("WindowStartUtc"));
                        candidate.WindowEndUtc = Utc(Cell("WindowEndUtc"));
                        if (candidate.WindowEndUtc < candidate.WindowStartUtc)
                            throw new FormatException("window end precedes start.");
                    }
                    if (Cell("SampleCount").Length > 0)
                    {
                        if (!int.TryParse(Cell("SampleCount"), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1)
                            throw new FormatException("SampleCount must be a positive integer.");
                        candidate.SampleCount = count;
                    }
                    if (Cell("P95Ms").Length > 0)
                    {
                        if (!double.TryParse(Cell("P95Ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ||
                            double.IsInfinity(duration) || double.IsNaN(duration) || duration < 0)
                            throw new FormatException("P95Ms must be a finite nonnegative duration.");
                        if (candidate.MetricKind != "PageLoad" && candidate.MetricKind != "ServerRequest")
                            throw new FormatException("timings require MetricKind PageLoad or ServerRequest.");
                        if (!candidate.SampleCount.HasValue || !candidate.WindowStartUtc.HasValue)
                            throw new FormatException("timings require SampleCount and a UTC time window.");
                        candidate.P95Ms = duration;
                    }
                    else if (candidate.MetricKind.Length > 0)
                        throw new FormatException("MetricKind requires a timing value.");
                    var key = string.Join("|", required.Select(Cell));
                    if (!seen.Add(key)) result.DuplicateCount++;
                    else result.Candidates.Add(candidate);
                }
                catch (FormatException ex) { result.Errors.Add(rowLabel + ex.Message); }
            }
            return result;
        }

        public static string EncodeCell(string value)
        {
            var trimmed = value.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0)
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static Guid NonEmptyGuid(string value)
        {
            if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
                throw new FormatException("expected a nonempty GUID.");
            return parsed;
        }

        private static DateTimeOffset Utc(string value)
        {
            var formats = new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" };
            if (!DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                throw new FormatException("timestamps must be ISO 8601 UTC values ending in Z.");
            return parsed;
        }

        private static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            var cells = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            var closedQuote = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') { quoted = false; closedQuote = true; }
                    else cell.Append(c);
                    continue;
                }
                if (c == '"' && cell.Length == 0 && !closedQuote) { quoted = true; continue; }
                if (c == ',' || c == '\r' || c == '\n')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                    closedQuote = false;
                    if (c == ',') continue;
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    rows.Add(cells.ToArray());
                    cells.Clear();
                    if (rows.Count > 10001) throw new FormatException("CSV exceeds the 10,000-candidate limit.");
                }
                else
                {
                    if (closedQuote || c == '"') throw new FormatException("Malformed CSV quoting.");
                    cell.Append(c);
                }
            }
            if (quoted) throw new FormatException("Unterminated quoted CSV field.");
            if (cell.Length > 0 || cells.Count > 0 || closedQuote)
            {
                cells.Add(cell.ToString());
                rows.Add(cells.ToArray());
            }
            if (rows.Count > 10001) throw new FormatException("CSV exceeds the 10,000-candidate limit.");
            return rows;
        }
    }
}
