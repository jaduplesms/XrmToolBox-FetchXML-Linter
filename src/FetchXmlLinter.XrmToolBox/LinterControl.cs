using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FetchXmlLinter.Core;
using Microsoft.Xrm.Sdk;
using McTools.Xrm.Connection;
using Label = System.Windows.Forms.Label;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using XrmToolBox.Extensibility;

namespace FetchXmlLinter.XrmToolBox
{
    internal sealed class AnalysisEntry
    {
        public string SourceName { get; set; } = "";
        public ViewSource? Source { get; set; }
        public AnalysisResult Result { get; set; } = new AnalysisResult();
        public DateTime AnalysedAtUtc { get; set; }
        public override string ToString() => SourceName + " | " + Result.Status + " | " + Result.Findings.Count + " findings";
    }

    public sealed class LinterControl : PluginControlBase
    {
        private readonly TextBox xml = Editor("FetchXML");
        private readonly TextBox layout = Editor("Optional layout XML");
        private readonly TextBox search = new TextBox { Width = 230, AccessibleName = "User name prefix or Dataverse user ID" };
        private readonly TextBox outcome = Editor("Collection outcomes");
        private readonly TextBox detail = Editor("Finding evidence and limitations");
        private readonly TextBox kql = Editor("KQL template");
        private readonly CheckBox connectedMetadata = new CheckBox { Text = "Use connected metadata", AutoSize = true };
        private readonly CheckedListBox users = Picker("Users");
        private readonly CheckedListBox views = Picker("Personal views to analyse");
        private readonly CheckedListBox shortlist = Picker("Validated CSV candidates");
        private readonly ListBox results = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Analysis results" };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 40, Text = "Alpha: read-only static analysis. No query execution or saved-view changes.", Padding = new Padding(5) };
        private readonly TabControl inputs = new TabControl { Dock = DockStyle.Fill };
        private readonly List<Button> actions = new List<Button>();
        private readonly List<AnalysisEntry> entries = new List<AnalysisEntry>();
        private readonly List<string> collectionOutcomes = new List<string>();
        private readonly Button cancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        private CancellationTokenSource? cancellation;
        private int generation;

        public LinterControl()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(244, 248, 252);
            Font = new Font("Segoe UI", 9);
            outcome.ReadOnly = true;
            detail.ReadOnly = true;
            kql.ReadOnly = true;

            var manual = new TabPage("Manual FetchXML");
            var manualSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            manualSplit.Panel1.Controls.Add(xml);
            manualSplit.Panel2.Controls.Add(layout);
            var manualCommands = Commands();
            manualCommands.Controls.Add(Action("Open FetchXML...", OpenXml));
            manualCommands.Controls.Add(connectedMetadata);
            manualCommands.Controls.Add(Action("Analyse XML", AnalyseManual));
            manual.Controls.Add(manualSplit);
            manual.Controls.Add(manualCommands);

            var personal = new TabPage("Personal views");
            var pickers = new SplitContainer { Dock = DockStyle.Fill };
            pickers.Panel1.Controls.Add(users);
            pickers.Panel2.Controls.Add(views);
            var personalCommands = Commands();
            personalCommands.Controls.Add(search);
            personalCommands.Controls.Add(Action("Find users (first 200)", FindUsers));
            personalCommands.Controls.Add(Action("Collect checked users", CollectUsers));
            personalCommands.Controls.Add(Action("Analyse checked views", AnalyseViews));
            personalCommands.Controls.Add(Action("Open selected view XML", OpenViewXml));
            personal.Controls.Add(pickers);
            personal.Controls.Add(personalCommands);

            var csv = new TabPage("CSV shortlist");
            var csvCommands = Commands();
            csvCommands.Controls.Add(Action("Import CSV...", ImportCsv));
            csvCommands.Controls.Add(Action("Collect checked candidates", CollectCandidates));
            csv.Controls.Add(shortlist);
            csv.Controls.Add(csvCommands);

            var telemetry = new TabPage("Application Insights KQL");
            var templates = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "KQL template" };
            templates.Items.AddRange(new object[] { "exact-view-markers.kql", "user-table-candidates.kql" });
            templates.SelectedIndexChanged += (_, __) => LoadKql(templates.SelectedItem!.ToString()!);
            var telemetryCommands = Commands();
            telemetryCommands.Controls.Add(templates);
            telemetryCommands.Controls.Add(Action("Copy KQL", () => Clipboard.SetText(kql.Text)));
            telemetryCommands.Controls.Add(new Label { AutoSize = true, Text = "Run in Azure Logs; export results as CSV. See docs/telemetry.md." });
            telemetry.Controls.Add(kql);
            telemetry.Controls.Add(telemetryCommands);
            templates.SelectedIndex = 0;

            inputs.TabPages.AddRange(new[] { manual, personal, csv, telemetry });
            var output = new SplitContainer { Dock = DockStyle.Fill };
            output.Panel1.Controls.Add(results);
            output.Panel2.Controls.Add(detail);
            results.SelectedIndexChanged += (_, __) => ShowResult();
            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
            main.Controls.Add(inputs, 0, 0);
            main.Controls.Add(output, 0, 1);
            main.Controls.Add(outcome, 0, 2);
            var common = Commands();
            common.Controls.Add(Action("Export results JSON...", ExportJson));
            common.Controls.Add(Action("Export findings CSV...", ExportCsv));
            common.Controls.Add(cancel);
            cancel.Click += (_, __) => { cancellation?.Cancel(); status.Text = "Cancellation requested; an in-flight SDK read may need to finish."; };
            Controls.Add(main);
            Controls.Add(common);
            Controls.Add(status);
        }

        private static TextBox Editor(string name) => new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, AcceptsTab = true, MaxLength = Linter.MaximumXmlCharacters,
            Font = new Font("Consolas", 10), AccessibleName = name
        };

        private static CheckedListBox Picker(string name) => new CheckedListBox
        {
            Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true, AccessibleName = name
        };

        private static FlowLayoutPanel Commands() => new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4), WrapContents = true
        };

        private Button Action(string text, System.Action handler)
        {
            var button = new Button { Text = text, AutoSize = true, AccessibleName = text };
            button.Click += (_, __) =>
            {
                try { handler(); }
                catch (Exception ex) { ShowErrorDialog(ex); }
            };
            actions.Add(button);
            return button;
        }

        private void OpenXml()
        {
            using (var dialog = new OpenFileDialog { Filter = "XML files|*.xml|All files|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    xml.Text = ReadBounded(dialog.FileName, Linter.MaximumXmlCharacters);
        }

        private static string ReadBounded(string path, int maximum)
        {
            if (new FileInfo(path).Length > maximum * 4L)
                throw new InvalidOperationException("File exceeds the supported input size.");
            var text = File.ReadAllText(path);
            if (text.Length > maximum) throw new InvalidOperationException("File exceeds the supported character limit.");
            return text;
        }

        private void AnalyseManual()
        {
            collectionOutcomes.Clear();
            outcome.Clear();
            var source = xml.Text;
            var suppliedLayout = layout.Text;
            var first = new Linter().Analyze(source, layoutXml: suppliedLayout);
            if (connectedMetadata.Checked && first.Status != AnalysisStatus.Invalid)
            {
                ExecuteWithConnection(() => Connected(reader =>
                {
                    var errors = new List<string>();
                    var metadata = reader.GetMetadata(first.Tables, errors);
                    var result = new Linter().Analyze(source, metadata, suppliedLayout);
                    ApplyMetadataErrors(result, errors);
                    return new AnalysisEntry { SourceName = "Manual FetchXML", Result = result, AnalysedAtUtc = DateTime.UtcNow };
                }, entry => AddResults(new[] { entry })));
            }
            else
                AddResults(new[] { new AnalysisEntry { SourceName = "Manual FetchXML", Result = first, AnalysedAtUtc = DateTime.UtcNow } });
        }

        private void FindUsers()
        {
            var term = search.Text.Trim();
            if (term.Length < 2) throw new InvalidOperationException("Enter at least two name-prefix characters or a Dataverse user GUID.");
            ExecuteWithConnection(() => Connected(reader => reader.FindUsers(term), found =>
            {
                users.Items.Clear();
                users.Items.AddRange(found.Cast<object>().ToArray());
                status.Text = found.Count + " enabled Read/Write users shown (maximum 200). Refine the prefix if needed.";
            }));
        }

        private void CollectUsers()
        {
            var selected = users.CheckedItems.Cast<UserTarget>().ToList();
            if (selected.Count == 0) throw new InvalidOperationException("Check at least one user.");
            ExecuteWithConnection(() => Connected(reader => reader.Collect(selected), PresentCollection));
        }

        private void PresentCollection(CollectionResult collected)
        {
            views.Items.Clear();
            views.Items.AddRange(collected.Views.Cast<object>().ToArray());
            collectionOutcomes.Clear();
            collectionOutcomes.AddRange(collected.Outcomes);
            outcome.Text = string.Join(Environment.NewLine, collectionOutcomes);
            entries.Clear();
            results.Items.Clear();
            detail.Clear();
            inputs.SelectedIndex = 1;
            status.Text = (collected.Cancelled ? "Cancelled; partial collection. " : "") +
                collected.Views.Count + " candidate definitions. Check views to analyse; inspect coverage outcomes below.";
        }

        private void AnalyseViews()
        {
            var selected = views.CheckedItems.Cast<ViewSource>().ToList();
            if (selected.Count == 0) throw new InvalidOperationException("Check individual views or explicitly check the collected set.");
            ExecuteWithConnection(() => Connected(reader =>
            {
                var linter = new Linter();
                var tables = selected.SelectMany(v => linter.Analyze(v.Xml).Tables).Distinct().ToList();
                var errors = new List<string>();
                var metadata = reader.GetMetadata(tables, errors);
                return selected.Select(v =>
                {
                    var result = linter.Analyze(v.Xml, metadata, v.Layout);
                    ApplyMetadataErrors(result, errors);
                    return new AnalysisEntry { SourceName = v.Name, Source = v, Result = result, AnalysedAtUtc = DateTime.UtcNow };
                }).ToList();
            }, AddResults));
        }

        private static void ApplyMetadataErrors(AnalysisResult result, List<string> errors)
        {
            if (errors.Count == 0) return;
            result.NotEvaluated.AddRange(errors);
            if (result.Status != AnalysisStatus.Invalid) result.Status = AnalysisStatus.Incomplete;
        }

        private void OpenViewXml()
        {
            if (!(views.SelectedItem is ViewSource source)) throw new InvalidOperationException("Select a collected view.");
            xml.Text = source.Xml;
            layout.Text = source.Layout;
            inputs.SelectedIndex = 0;
        }

        private void ImportCsv()
        {
            using (var dialog = new OpenFileDialog { Filter = "CSV shortlist|*.csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var csv = ReadBounded(dialog.FileName, ShortlistCsv.MaximumCharacters);
                ExecuteWithConnection(() => Connected(reader => ShortlistCsv.Read(csv, reader.OrganizationId()), imported =>
                {
                    shortlist.Items.Clear();
                    shortlist.Items.AddRange(imported.Candidates.Cast<object>().ToArray());
                    outcome.Text = imported.Errors.Count == 0 ? "CSV validation completed." : string.Join(Environment.NewLine, imported.Errors);
                    status.Text = imported.Candidates.Count + " valid candidate rows; " + imported.Errors.Count +
                        " rejected rows; " + imported.DuplicateCount + " exact duplicates removed. Nothing is selected automatically.";
                }));
            }
        }

        private void CollectCandidates()
        {
            var candidates = shortlist.CheckedItems.Cast<Candidate>().ToList();
            if (candidates.Count == 0) throw new InvalidOperationException("Check validated candidate rows first.");
            ExecuteWithConnection(() => Connected(reader =>
            {
                var organization = reader.OrganizationId();
                if (candidates.Any(c => c.OrganizationId != organization))
                    throw new InvalidOperationException("Shortlist environment no longer matches the current connection.");
                var actors = new List<UserTarget>();
                var resolvedCandidates = new List<Candidate>();
                var resolutionErrors = new List<string>();
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var actor = reader.ResolveActor(candidate);
                        candidate.ResolvedDataverseUserId = actor.Id;
                        actors.Add(actor);
                        resolvedCandidates.Add(candidate);
                    }
                    catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
                    {
                        resolutionErrors.Add("Actor " + candidate.UserId + ": unresolved; " + ex.Detail.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        resolutionErrors.Add("Actor " + candidate.UserId + ": unresolved; " + ex.Message);
                    }
                }
                var collected = reader.Collect(actors, resolvedCandidates);
                collected.Outcomes.InsertRange(0, resolutionErrors);
                return collected;
            }, PresentCollection));
        }

        internal void ExecuteWithConnection(System.Action action) =>
            ExecuteMethod(callback => callback.ExternalAction(), new ExternalMethodCallerInfo(action));

        private void Connected<T>(Func<DataverseReader, T> work, Action<T> apply)
        {
            var original = ConnectionDetail.GetCrmServiceClient();
            if (original == null) throw new InvalidOperationException("An OAuth Dataverse connection is required.");
            Run((token, progress) =>
            {
                var cloned = original.Clone();
                if (cloned == null || !cloned.IsReady)
                {
                    cloned?.Dispose();
                    throw new InvalidOperationException("The connection could not be cloned. Use an OAuth connection; no shared caller state was modified.");
                }
                cloned.CallerId = Guid.Empty;
                using (var reader = new DataverseReader(cloned, token, progress))
                    return work(reader);
            }, apply);
        }

        private async void Run<T>(Func<CancellationToken, IProgress<string>, T> work, Action<T> apply)
        {
            if (cancellation != null) { status.Text = "Wait for the active operation to finish."; return; }
            var current = new CancellationTokenSource();
            cancellation = current;
            var startedGeneration = generation;
            foreach (var action in actions) action.Enabled = false;
            cancel.Enabled = true;
            var progress = new Progress<string>(message =>
            {
                if (!IsDisposed && generation == startedGeneration) status.Text = message;
            });
            try
            {
                var value = await Task.Run(() => work(current.Token, progress));
                if (IsDisposed || generation != startedGeneration) return;
                apply(value);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && generation == startedGeneration) status.Text = "Cancelled; operation incomplete. No saved query was executed or modified.";
            }
            catch (Exception ex)
            {
                if (!IsDisposed && generation == startedGeneration)
                {
                    status.Text = "Operation failed; do not treat this as an empty successful result.";
                    ShowErrorDialog(ex);
                }
            }
            finally
            {
                cancellation = null;
                current.Dispose();
                if (!IsDisposed)
                {
                    foreach (var action in actions) action.Enabled = true;
                    cancel.Enabled = false;
                }
            }
        }

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            generation++;
            cancellation?.Cancel();
            users.Items.Clear();
            views.Items.Clear();
            shortlist.Items.Clear();
            entries.Clear();
            results.Items.Clear();
            collectionOutcomes.Clear();
            outcome.Clear();
            this.detail.Clear();
            base.UpdateConnection(newService, detail, actionName, parameter);
            status.Text = "Connection changed. Collected state and shortlist cleared; old background results will be discarded.";
        }

        private void AddResults(IEnumerable<AnalysisEntry> analysed)
        {
            entries.Clear();
            entries.AddRange(analysed);
            results.Items.Clear();
            results.Items.AddRange(entries.Cast<object>().ToArray());
            if (results.Items.Count > 0) results.SelectedIndex = 0;
            status.Text = entries.Count + " definitions analysed. No findings does not prove a query is fast.";
        }

        private void ShowResult()
        {
            if (!(results.SelectedItem is AnalysisEntry entry)) return;
            var text = new StringBuilder(entry.SourceName + Environment.NewLine + "Status: " + entry.Result.Status +
                Environment.NewLine + "SHA-256: " + entry.Result.SourceHash + Environment.NewLine);
            if (entry.Result.Error.Length > 0) text.AppendLine(entry.Result.Error);
            foreach (var finding in entry.Result.Findings)
                text.AppendLine().AppendLine(finding.RuleId + " | " + finding.Category + " | " + finding.Severity)
                    .AppendLine(finding.Location + " (line " + finding.Line + ")")
                    .AppendLine("Evidence: " + finding.Evidence).AppendLine(finding.Explanation)
                    .AppendLine("Review: " + finding.Recommendation).AppendLine("Limit: " + finding.Limitation)
                    .AppendLine(finding.DocumentationUrl);
            foreach (var missing in entry.Result.NotEvaluated) text.AppendLine("NOT EVALUATED: " + missing);
            if (entry.Source?.Candidate != null)
            {
                var candidate = entry.Source.Candidate;
                text.AppendLine().AppendLine("Imported candidate: " + candidate.Attribution +
                    "; " + candidate.MetricKind + " p95=" + candidate.P95Ms +
                    " ms; samples=" + candidate.SampleCount)
                    .AppendLine("Observed duration is not a proven SQL execution time or a measured optimization benefit.");
            }
            detail.Text = text.ToString();
        }

        private void LoadKql(string name)
        {
            var assembly = typeof(LinterControl).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name, StringComparison.Ordinal));
            using (var stream = assembly.GetManifestResourceStream(resource))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("KQL template is missing.")))
                kql.Text = reader.ReadToEnd();
        }

        private void ExportJson()
        {
            if (entries.Count == 0) throw new InvalidOperationException("Analyse at least one definition first.");
            using (var dialog = new SaveFileDialog { Filter = "JSON results|*.json", FileName = "fetchxml-findings.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var report = new
                {
                    SchemaVersion = 1,
                    GeneratedAtUtc = DateTime.UtcNow,
                    CollectionOutcomes = collectionOutcomes,
                    Definitions = entries.Select(e => new
                    {
                        e.SourceName, e.AnalysedAtUtc, ViewId = e.Source?.ViewId, OwnerId = e.Source?.OwnerId,
                        ModifiedOnUtc = e.Source?.ModifiedOnUtc, Candidate = e.Source?.Candidate, e.Result
                    })
                };
                File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(report, Formatting.Indented,
                    new StringEnumConverter()), new UTF8Encoding(false));
                status.Text = "JSON exported. Review local reports before sharing; identifiers and evidence can be sensitive.";
            }
        }

        private void ExportCsv()
        {
            if (entries.Count == 0) throw new InvalidOperationException("Analyse at least one definition first.");
            using (var dialog = new SaveFileDialog { Filter = "CSV findings|*.csv", FileName = "fetchxml-findings.csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var output = new StringBuilder("Source,Status,SourceHash,RuleId,Category,Severity,Location,Evidence,Explanation,Recommendation,Limitation,DocumentationUrl\r\n");
                foreach (var entry in entries)
                {
                    if (entry.Result.Findings.Count == 0)
                        output.AppendLine(string.Join(",", new[] { entry.SourceName, entry.Result.Status.ToString(), entry.Result.SourceHash,
                            "", "", "", "", entry.Result.Error, "", "", string.Join("; ", entry.Result.NotEvaluated), "" }.Select(ShortlistCsv.EncodeCell)));
                    foreach (var finding in entry.Result.Findings)
                        output.AppendLine(string.Join(",", new[] { entry.SourceName, entry.Result.Status.ToString(), entry.Result.SourceHash,
                            finding.RuleId, finding.Category.ToString(), finding.Severity.ToString(), finding.Location, finding.Evidence,
                            finding.Explanation, finding.Recommendation, finding.Limitation, finding.DocumentationUrl }.Select(ShortlistCsv.EncodeCell)));
                }
                File.WriteAllText(dialog.FileName, output.ToString(), new UTF8Encoding(true));
                status.Text = "Findings CSV exported. JSON includes fuller coverage and telemetry provenance.";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) cancellation?.Cancel();
            base.Dispose(disposing);
        }
    }
}
