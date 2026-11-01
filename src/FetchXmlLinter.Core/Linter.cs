using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FetchXmlLinter.Core
{
    public sealed partial class Linter
    {
        public const int MaximumXmlCharacters = 1024 * 1024;
        private const string AntiPatterns = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/query-antipatterns";
        private const string Performance = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/optimize-performance";
        private const string Ordering = "https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/order-rows";
        private static readonly HashSet<string> Nodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "fetch", "entity", "attribute", "all-attributes", "link-entity",
            "filter", "condition", "value", "order"
        };

        public AnalysisResult Analyze(string xml, IMetadataProvider? metadata = null, string? layoutXml = null,
            IEnumerable<string>? enabledRuleIds = null, AnalysisSurface surface = AnalysisSurface.OnlineQuery)
        {
            var enabled = new HashSet<string>(enabledRuleIds ?? RuleCatalog.All.Select(r => r.Id), StringComparer.Ordinal);
            if (enabled.Count == 0 || enabled.Any(id => !RuleCatalog.All.Any(r => r.Id == id)))
                throw new ArgumentException("Select at least one known rule ID from the rule catalog.", nameof(enabledRuleIds));
            var result = new AnalysisResult { Surface = surface };
            result.SelectedRuleIds.AddRange(RuleCatalog.All.Where(r => enabled.Contains(r.Id)).Select(r => r.Id));
            if (surface == AnalysisSurface.OfflineProfile)
            {
                result.SkippedRuleIds.AddRange(result.SelectedRuleIds.Where(id => !RuleCatalog.SupportsOfflineProfile(id)));
                enabled.ExceptWith(result.SkippedRuleIds);
                Incomplete(result, "Offline profile review covers configured filters only. Downloaded columns come from Filter Columns, not FetchXML projection. Relationship scope, row volume, device SQL and sync benefit are not evaluated.");
                if (result.SkippedRuleIds.Count > 0)
                    Incomplete(result, "Online projection, sorting, paging, hint and unsupported join rules skipped for offline profiles: " +
                        string.Join(", ", result.SkippedRuleIds));
            }
            if (xml == null || xml.Length == 0 || xml.Length > MaximumXmlCharacters)
            {
                result.Status = AnalysisStatus.Invalid;
                result.Error = "FetchXML must contain between 1 and 1,048,576 characters.";
                return result;
            }
            using (var sha = SHA256.Create())
                result.SourceHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(xml))).Replace("-", "").ToLowerInvariant();

            XDocument document;
            XDocument? layout = null;
            try
            {
                document = Parse(xml);
                if (!string.IsNullOrWhiteSpace(layoutXml))
                    layout = Parse(layoutXml!);
            }
            catch (XmlException ex)
            {
                result.Status = AnalysisStatus.Invalid;
                result.Error = "Invalid or unsafe XML: " + ex.Message;
                return result;
            }
            var root = document.Root;
            if (root == null || root.Name != "fetch" || root.Elements("entity").Count() != 1)
            {
                result.Status = AnalysisStatus.Invalid;
                result.Error = "Expected an unqualified <fetch> root with exactly one <entity>.";
                return result;
            }
            if (document.Descendants().Count() > 10000 || document.Descendants().Any(e => e.Ancestors().Take(65).Count() > 64))
            {
                result.Status = AnalysisStatus.Invalid;
                result.Error = "XML exceeds the supported depth (64) or element count (10,000).";
                return result;
            }
            foreach (var unknown in root.Descendants().Where(e => e.Name.NamespaceName.Length != 0 || !Nodes.Contains(e.Name.LocalName)))
                Incomplete(result, "Unsupported element: " + Location(unknown));
            if (root.Attribute("aggregate")?.Value == "true")
                Incomplete(result, "Aggregate FetchXML is not fully evaluated by this rule set.");
            if (root.Descendants("attribute").Any(a => a.Attribute("aggregate") != null || a.Attribute("groupby") != null))
                Incomplete(result, "Aggregate/group-by column semantics are not fully evaluated.");

            result.Table = root.Element("entity")!.Attribute("name")?.Value ?? "";
            var entities = root.Descendants().Where(IsEntity).ToList();
            if (entities.Any(e => string.IsNullOrWhiteSpace(e.Attribute("name")?.Value)))
            {
                result.Status = AnalysisStatus.Invalid;
                result.Error = "Every entity and link-entity must have a table name.";
                return result;
            }
            result.Tables.AddRange(entities.Select(e => e.Attribute("name")!.Value).Distinct(StringComparer.OrdinalIgnoreCase));
            AnalyzeShape(root, entities, result, enabled);
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ambiguousAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in entities)
            {
                var alias = entity.Attribute("alias")?.Value;
                if (alias == null) continue;
                if (aliases.ContainsKey(alias))
                {
                    ambiguousAliases.Add(alias);
                    Incomplete(result, "Duplicate table alias: " + alias);
                }
                else aliases.Add(alias, entity.Attribute("name")!.Value);
            }

            foreach (var entity in entities)
            {
                if (enabled.Contains("FXL001") && (entity.Element("all-attributes") != null ||
                    (entity.Name == "entity" && !entity.Elements("attribute").Any())))
                {
                    Add(result, "FXL001", entity, "All-column or implicit projection on " + entity.Attribute("name")!.Value,
                        "Broad projection can retrieve columns the consumer does not need.",
                        "Review the effective projection and explicitly select required columns.",
                        "Projection semantics depend on query shape and execution surface; preserve required grid/filter/order dependencies.", AntiPatterns);
                    Incomplete(result, "Unexpanded all-column projection: individual projected columns were not evaluated at " + Location(entity));
                }
            }

            foreach (var condition in root.Descendants("condition"))
            {
                var op = condition.Attribute("operator")?.Value ?? "";
                var value = condition.Attribute("value")?.Value;
                if (enabled.Contains("FXL002") && (op == "ends-with" || (op == "like" && value != null && HasLeadingWildcard(value))))
                    Add(result, "FXL002", condition, "operator=" + op + "; leading wildcard/suffix pattern",
                        "Leading-wildcard filtering is a documented performance risk.",
                        "Consider exact/prefix search only if the business requirement permits it.",
                        "Changing contains/suffix matching changes results. No performance benefit has been measured.", AntiPatterns);
            }

            foreach (var node in root.Descendants().Where(e =>
                (e.Name == "attribute" && enabled.Contains("FXL005")) ||
                (e.Name == "condition" && (enabled.Contains("FXL003") || enabled.Contains("FXL004"))) ||
                (e.Name == "order" && (enabled.Contains("FXL006") || enabled.Contains("FXL007")))))
            {
                var column = node.Attribute(node.Name == "attribute" ? "name" : "attribute")?.Value;
                if (string.IsNullOrWhiteSpace(column))
                {
                    Incomplete(result, "Missing column reference at " + Location(node));
                    continue;
                }
                var context = node.Ancestors().FirstOrDefault(IsEntity);
                if (context == null)
                {
                    Incomplete(result, "Missing entity context at " + Location(node));
                    continue;
                }
                var table = context.Attribute("name")!.Value;
                var entityName = node.Attribute("entityname")?.Value;
                if (entityName != null)
                {
                    if (ambiguousAliases.Contains(entityName) || !aliases.TryGetValue(entityName, out var resolved))
                    {
                        Incomplete(result, "Unresolved table alias '" + entityName + "' at " + Location(node));
                        continue;
                    }
                    table = resolved;
                }
                var field = metadata?.FindColumn(table, column!);
                if (field == null)
                {
                    Incomplete(result, "Column metadata unavailable: " + table + "." + column);
                    continue;
                }
                var evidence = table + "." + column + " (" + field.Kind + ")";
                if (node.Name == "condition")
                {
                    if (enabled.Contains("FXL003") && field.IsCalculated == true)
                        Add(result, "FXL003", node, evidence,
                            "Filtering on calculated/formula values may require computation before filtering.",
                            "Review a stored/searchable alternative with the table owner.",
                            "An alternative must preserve business semantics; calculation behavior is metadata-dependent.", AntiPatterns);
                    else if (enabled.Contains("FXL003") && field.IsCalculated == null)
                        Incomplete(result, "Calculation classification unavailable: " + table + "." + column);
                    if (enabled.Contains("FXL004") && (field.Kind == ColumnKind.Memo || (field.Kind == ColumnKind.String && field.MaxLength > 850)))
                        Add(result, "FXL004", node, evidence + "; MaxLength=" + field.MaxLength,
                            "Filtering large-text columns is a documented query anti-pattern.",
                            "Review the filtering requirement and a suitable search experience.",
                            "Dataverse Search is not a drop-in equivalent to this FetchXML filter.", AntiPatterns);
                    if (enabled.Contains("FXL004") && field.Kind == ColumnKind.String && field.MaxLength == null)
                        Incomplete(result, "String maximum length unavailable: " + table + "." + column);
                }
                if (node.Name == "attribute" && field.Kind == ColumnKind.Memo && node.Attribute("aggregate") == null)
                {
                    var layoutName = node.Attribute("alias")?.Value ??
                        (context.Name == "entity" ? column : context.Attribute("alias")?.Value + "." + column);
                    var visible = layout?.Descendants("cell").Any(c => c.Attribute("name")?.Value == layoutName);
                    Add(result, "FXL005", node, evidence + (visible == true ? "; present in supplied layout" : ""),
                        "The projection includes a full large-text value.",
                        "If the list needs only a summary, review removing the body column from both FetchXML and the consumer layout.",
                        "The content may be required. Removing it changes displayed information; benefit is unmeasured.", Performance,
                        FindingCategory.ContextualReview, Severity.Information);
                }
                if (node.Name == "order")
                {
                    if (enabled.Contains("FXL006") && field.Kind == ColumnKind.Choice &&
                        root.Attribute("useraworderby")?.Value != "true" && root.Attribute("useraworderby")?.Value != "1")
                        Add(result, "FXL006", node, evidence,
                            "Choice ordering ordinarily uses localized labels.",
                            "Review whether label-based ordering is required.",
                            "Ordering by integer values changes ordering semantics.", Ordering);
                    if (enabled.Contains("FXL007") && (field.Kind == ColumnKind.Lookup || context.Name == "link-entity" || entityName != null))
                        Add(result, "FXL007", node, evidence,
                            "Lookup/related-table ordering can require additional related-data work.",
                            "Review whether this ordering is necessary for the consumer.",
                            "Removing/changing sorting changes the global result order and paging.", Ordering,
                            FindingCategory.ContextualReview, Severity.Information);
                }
            }

            foreach (var option in root.Attributes().Where(a => enabled.Contains("FXL008") &&
                (a.Name == "options" || a.Name == "latematerialize" || a.Name == "no-lock")))
                Add(result, "FXL008", root, option.Name + "=" + option.Value,
                    option.Name == "options" ? "SQL query options require Microsoft support guidance." :
                    option.Name == "no-lock" ? "no-lock is a legacy setting and is no longer necessary." :
                    "Late materialization can help or hurt depending on query shape and workload.",
                    "Review existing settings against the documented guidance; do not change them automatically.",
                    "Presence alone is not evidence of a defect or benefit.", Performance,
                    FindingCategory.ContextualReview, Severity.Information);
            foreach (var filter in root.Descendants("filter").Where(f => enabled.Contains("FXL008") && f.Attribute("hint") != null))
                Add(result, "FXL008", filter, "hint=" + filter.Attribute("hint")!.Value,
                    "Filter hints have specific applicability and nesting restrictions.",
                    "Review the documented restrictions without changing grouping or join semantics.",
                    "This rule inventories the setting; it does not prove the hint was applied or helped.", Performance,
                    FindingCategory.ContextualReview, Severity.Information);
            AnalyzeProjection(root, metadata, result, enabled);
            AnalyzeJoinKeys(root, metadata, result, enabled);
            return result;
        }

        private static XDocument Parse(string xml)
        {
            if (xml.Length > MaximumXmlCharacters)
                throw new XmlException("XML exceeds the 1 MiB character limit.");
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumXmlCharacters
            };
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
                return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }

        private static bool HasLeadingWildcard(string value)
        {
            if (value.Length == 0) return false;
            if (value[0] == '%' || value[0] == '_') return true;
            if (value.StartsWith("[^", StringComparison.Ordinal)) return true;
            return (value[0] == '-' || value[0] == '\'') && value.Length > 1 && value[1] == '%';
        }

        private static bool IsEntity(XElement element) => element.Name == "entity" || element.Name == "link-entity";

        private static string Location(XElement element) =>
            "/" + string.Join("/", element.AncestorsAndSelf().Reverse().Select(e =>
                e.Name.LocalName + "[" + (e.ElementsBeforeSelf(e.Name).Count() + 1) + "]"));

        private static void Incomplete(AnalysisResult result, string message)
        {
            if (!result.NotEvaluated.Contains(message)) result.NotEvaluated.Add(message);
            result.Status = AnalysisStatus.Incomplete;
        }

        private static void Add(AnalysisResult result, string id, XElement node, string evidence,
            string explanation, string recommendation, string limitation, string url,
            FindingCategory category = FindingCategory.DocumentedRisk, Severity severity = Severity.Warning)
        {
            result.Findings.Add(new Finding
            {
                RuleId = id, Category = category, Severity = severity, Location = Location(node),
                Line = ((IXmlLineInfo)node).LineNumber, Evidence = evidence, Explanation = explanation,
                Recommendation = recommendation, Limitation = limitation, DocumentationUrl = url
            });
        }
    }
}
