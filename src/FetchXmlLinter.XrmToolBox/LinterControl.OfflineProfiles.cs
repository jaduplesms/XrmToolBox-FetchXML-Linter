using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using FetchXmlLinter.Core;

namespace FetchXmlLinter.XrmToolBox
{
    public sealed partial class LinterControl
    {
        private readonly ComboBox offlineProfiles = new ComboBox
        {
            Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Offline profiles"
        };
        private readonly CheckedListBox offlineItems = Picker("Offline profile tables");
        private readonly XmlEditor offlineXml = new XmlEditor("Configured offline FetchXML") { ReadOnly = true };
        private readonly TextBox offlineScope = ReadableText("Offline scope and columns");
        private readonly ComboBox generatedFilters = new ComboBox
        {
            Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Generated filter records"
        };
        private readonly XmlEditor generatedXml = new XmlEditor("Generated offline FetchXML") { ReadOnly = true };
        private readonly XmlEditor generatedOuterXml = new XmlEditor("Generated outer FetchXML") { ReadOnly = true };
        private readonly TextBox generatedSql = Editor("Generated offline SQL");
        private readonly TextBox referencedSql = Editor("Referenced view SQL");
        private readonly Label offlineCount = new Label
        {
            Dock = DockStyle.Top, Height = 22, AutoEllipsis = true, AccessibleName = "Offline profile coverage",
            Text = "Load profiles, choose one, then load its tables. Nothing is selected automatically."
        };
        private readonly Label offlineDefinitionState = new Label
        {
            Dock = DockStyle.Top, Height = 22, AutoEllipsis = true, AccessibleName = "Offline definition status",
            Text = "Highlight a table to inspect its definition."
        };
        private readonly Label generatedState = new Label
        {
            Dock = DockStyle.Top, Height = 22, AutoEllipsis = true, AccessibleName = "Generated definition coverage",
            Text = "Generated definitions have not been loaded."
        };
        private Button loadOfflineItems = null!;
        private Button analyseOffline = null!;
        private Button openOffline = null!;
        private Button loadGenerated = null!;

        private void BuildOfflinePage()
        {
            var profileRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            profileRow.Controls.Add(Action("Load profiles", () => ExecuteWithConnection(() =>
                Connected(reader => reader.LoadOfflineProfiles(), PresentOfflineProfiles, "Loading offline profiles")),
                "Read offline profile definitions using the connected user's privileges. No impersonation or profile changes."), 0, 0);
            profileRow.Controls.Add(offlineProfiles, 1, 0);
            loadOfflineItems = Action("Load tables", LoadOfflineItems,
                "Read all table items for the chosen profile, including configured filters and column/scope settings.", true);
            profileRow.Controls.Add(loadOfflineItems, 2, 0);
            offlineProfiles.Margin = new Padding(0, 8, 8, 0);

            var split = Split(Orientation.Vertical, 280);
            split.FixedPanel = FixedPanel.Panel1;
            split.Panel1MinSize = 220;
            split.Panel2MinSize = 220;
            split.AccessibleName = "Offline profile selectors";
            var tableCommands = Commands();
            tableCommands.WrapContents = false;
            tableCommands.Controls.Add(Action("Check all", () =>
            {
                for (var index = 0; index < offlineItems.Items.Count; index++) offlineItems.SetItemChecked(index, true);
            }, "Check every loaded table, including criteria-only items so analysis reports their coverage."));
            tableCommands.Controls.Add(Action("Uncheck all", () =>
            {
                for (var index = 0; index < offlineItems.Items.Count; index++) offlineItems.SetItemChecked(index, false);
            }, "Uncheck all loaded offline table items."));
            var analysisCommands = Commands();
            analysisCommands.Dock = DockStyle.Bottom;
            analyseOffline = Action("Analyse tables", AnalyseOfflineItems,
                "Analyse all checked configured filters with offline-applicable rules. Criteria-only and unreadable items remain explicit coverage gaps.", true);
            analysisCommands.Controls.Add(analyseOffline);
            var tablePanel = new Panel { Dock = DockStyle.Fill };
            tablePanel.Controls.Add(offlineItems);
            tablePanel.Controls.Add(tableCommands);
            tablePanel.Controls.Add(analysisCommands);
            split.Panel1.Controls.Add(Section("Check tables to analyse", tablePanel));
            var inspection = new TabControl { Dock = DockStyle.Fill, AccessibleName = "Offline definition tabs" };
            var configured = new Panel { Dock = DockStyle.Fill };
            configured.Controls.Add(offlineXml);
            configured.Controls.Add(offlineDefinitionState);
            AddOfflineTab(inspection, "Configured FetchXML", configured);
            AddOfflineTab(inspection, "Scope & columns", offlineScope);
            var generatedTabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "Generated definition tabs" };
            AddOfflineTab(generatedTabs, "FetchXML", generatedXml);
            AddOfflineTab(generatedTabs, "Outer FetchXML", generatedOuterXml);
            generatedSql.ReadOnly = referencedSql.ReadOnly = true;
            AddOfflineTab(generatedTabs, "Generated offline SQL", generatedSql);
            AddOfflineTab(inspection, "Referenced view SQL", referencedSql);
            var generated = new Panel { Dock = DockStyle.Fill };
            generated.Controls.Add(generatedTabs);
            generated.Controls.Add(generatedState);
            generated.Controls.Add(generatedFilters);
            AddOfflineTab(inspection, "Generated definitions", generated);
            var body = new Panel { Dock = DockStyle.Fill };
            body.Controls.Add(split);
            body.Controls.Add(offlineCount);
            body.Controls.Add(profileRow);
            var commands = Commands();
            openOffline = Action("Open filter XML", OpenOfflineXml,
                "Open exactly one checked table's configured FetchXML in FetchXML query as a local copy. Offline rule scope is retained; no write-back.");
            loadGenerated = Action("Load generated definitions", LoadOfflineGenerated,
                "Read generated filter records for the highlighted table, not the checked set. Inspect only; SQL and generated queries are never executed or linted.");
            commands.Controls.Add(openOffline);
            commands.Controls.Add(loadGenerated);
            commands.Dock = DockStyle.Bottom;
            var inspectionPanel = new Panel { Dock = DockStyle.Fill };
            inspectionPanel.Controls.Add(inspection);
            inspectionPanel.Controls.Add(commands);
            split.Panel2.Controls.Add(inspectionPanel);
            pages.Add(WorkflowPage.OfflineProfiles, Page("Offline profiles",
                "Read-only profile review. Highlight a table to inspect; check tables to analyse. Generated offline SQL is separate from configured FetchXML.",
                body, Commands()));
            offlineProfiles.SelectedIndexChanged += (_, __) => ClearOfflineItems();
            offlineItems.SelectedIndexChanged += (_, __) => ShowOfflineItem();
            offlineItems.ItemCheck += (_, args) => UpdateOfflineActions(offlineItems.CheckedItems.Count +
                (args.NewValue == CheckState.Checked ? 1 : 0) - (args.CurrentValue == CheckState.Checked ? 1 : 0),
                args.Index, args.NewValue == CheckState.Checked);
            generatedFilters.SelectedIndexChanged += (_, __) => ShowGeneratedFilter();
            UpdateOfflineActions();
        }

        private static void AddOfflineTab(TabControl tabs, string name, Control content)
        {
            var page = new TabPage(name);
            page.Controls.Add(content);
            tabs.TabPages.Add(page);
        }

        private void PresentOfflineProfiles(List<OfflineProfile> profiles)
        {
            offlineProfiles.Items.Clear();
            offlineProfiles.Items.AddRange(profiles.Cast<object>().ToArray());
            ClearOfflineItems();
            status.Text = profiles.Count + " profiles loaded. Choose a profile and load its tables. Read-only.";
        }

        private void LoadOfflineItems()
        {
            if (!(offlineProfiles.SelectedItem is OfflineProfile profile)) return;
            ClearOfflineItems();
            ExecuteWithConnection(() => Connected(reader => reader.LoadOfflineItems(profile), PresentOfflineItems,
                "Loading offline profile tables"));
        }

        private void PresentOfflineItems(List<OfflineProfileItem> items)
        {
            offlineItems.Items.Clear();
            offlineItems.Items.AddRange(items.Cast<object>().ToArray());
            var configured = items.Count(i => !string.IsNullOrWhiteSpace(i.ConfiguredXml));
            var referenced = items.Count(i => string.IsNullOrWhiteSpace(i.ConfiguredXml) && !string.IsNullOrWhiteSpace(i.ReferencedViewXml));
            offlineCount.Text = items.Count + " table items | " + configured + " configured filters | " +
                referenced + " referenced-view filters | " + (items.Count - configured - referenced) + " without explicit FetchXML";
            tooltips.SetToolTip(offlineCount, offlineCount.Text);
            collectionOutcomes.Clear();
            collectionOutcomes.Add(offlineCount.Text + ". Definition presence is not XML validation or measured sync performance.");
            collectionOutcomes.AddRange(items.Where(i => i.ReferencedViewError.Length > 0)
                .Select(i => i.EntityName + ": " + i.ReferencedViewError));
            outcome.Text = string.Join(Environment.NewLine, collectionOutcomes);
            ClearAnalysis();
            status.Text = "Offline table definitions loaded. Check tables to analyse; highlight a table to inspect.";
            UpdateOfflineActions();
        }

        private void ShowOfflineItem()
        {
            ClearGeneratedDefinitions();
            offlineXml.Clear();
            offlineScope.Clear();
            referencedSql.Clear();
            if (offlineItems.SelectedItem is OfflineProfileItem item)
            {
                var warning = string.IsNullOrWhiteSpace(item.Xml) ? "" : offlineXml.LoadXml(item.Xml);
                offlineDefinitionState.Text = item.SourceField + (warning.Length == 0 ? "" : " | " + warning);
                tooltips.SetToolTip(offlineDefinitionState, offlineDefinitionState.Text);
                referencedSql.Text = item.ViewQuery;
                var profile = offlineProfiles.SelectedItem as OfflineProfile;
                offlineScope.Text = item.ProfileName + " | " + item.EntityName + Environment.NewLine +
                    "Profile managed: " + profile?.IsManaged + "; validated: " + profile?.IsValidated + Environment.NewLine +
                    "Distribution: " + item.Distribution + " (" + item.DistributionCriteria + ")" + Environment.NewLine +
                    "Related records: " + item.GetRelatedRecords + Environment.NewLine +
                    "Owned by me / business unit / team: " + item.OwnedByMe + " / " + item.OwnedByBusinessUnit + " / " + item.OwnedByTeam +
                    Environment.NewLine + "Sync interval (minutes): " + (item.SyncIntervalMinutes?.ToString() ?? "unspecified") +
                    Environment.NewLine + "Filter source: " + item.SourceField + Environment.NewLine + item.ReferencedViewError +
                    Environment.NewLine + Environment.NewLine + "Filter Columns (raw; not inferred from <all-attributes/>):" +
                    Environment.NewLine + item.SelectedColumns + Environment.NewLine + Environment.NewLine +
                    "Relationship configuration (raw; internal format is not interpreted):" + Environment.NewLine + item.RelationshipData +
                    Environment.NewLine + Environment.NewLine +
                    "Filters and relationship scope can combine. Static review does not prove effective row volume, downloaded columns, or sync benefit.";
            }
            UpdateOfflineActions();
        }

        private void LoadOfflineGenerated()
        {
            if (!(offlineItems.SelectedItem is OfflineProfileItem item)) return;
            ClearGeneratedDefinitions();
            ExecuteWithConnection(() => Connected(reader => reader.LoadOfflineGeneratedFilters(item), filters =>
            {
                generatedFilters.Items.AddRange(filters.Cast<object>().ToArray());
                generatedState.Text = filters.Count + " generated records loaded for " + item.EntityName +
                    ". Diagnostic definitions only; no SQL execution or linting.";
                tooltips.SetToolTip(generatedState, generatedState.Text);
                if (filters.Count > 0) generatedFilters.SelectedIndex = 0;
                status.Text = generatedState.Text;
            }, "Loading generated offline definitions"));
        }

        private void ShowGeneratedFilter()
        {
            if (!(generatedFilters.SelectedItem is OfflineGeneratedFilter filter)) return;
            var warning = generatedXml.LoadXml(filter.FetchXml);
            var outerWarning = generatedOuterXml.LoadXml(filter.OuterFetchXml);
            tooltips.SetToolTip(generatedOuterXml, outerWarning);
            generatedSql.Text = filter.OfflineSql;
            generatedState.Text = "Generated record " + filter.Id + " | " +
                (string.IsNullOrWhiteSpace(filter.FetchXml) ? "No FetchXML payload" :
                    warning.Length > 0 || !HasFetchRoot(filter.FetchXml) ? "Non-FetchXML/invalid payload retained as supplied (may be a placeholder)" : "FetchXML payload") +
                " | " + (string.IsNullOrWhiteSpace(filter.OfflineSql) ? "No offline SQL" : "Generated offline SQL present") +
                ". Inspection only.";
            tooltips.SetToolTip(generatedState, generatedState.Text);
        }

        private static bool HasFetchRoot(string source)
        {
            using (var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = Linter.MaximumXmlCharacters
            }))
            {
                reader.MoveToContent();
                return reader.LocalName == "fetch" && reader.NamespaceURI.Length == 0;
            }
        }

        private void AnalyseOfflineItems()
        {
            var selectedRules = SelectedRules();
            if (!CheckRuleSelection(selectedRules)) return;
            var selected = offlineItems.CheckedItems.Cast<OfflineProfileItem>().ToList();
            if (selected.Count == 0) { status.Text = "Check at least one offline table to analyse."; return; }
            List<AnalysisEntry> Analyse(DataverseReader? reader, CancellationToken token, IProgress<string>? progress)
            {
                var metadataErrors = new List<string>();
                var metadata = reader?.GetMetadata(selected.Where(i => !string.IsNullOrWhiteSpace(i.Xml))
                    .SelectMany(i => new Linter().Analyze(i.Xml, enabledRuleIds: selectedRules, surface: AnalysisSurface.OfflineProfile).Tables),
                    metadataErrors);
                var analysed = new List<AnalysisEntry>();
                foreach (var item in selected)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report("Reviewing offline table " + item.EntityName);
                    AnalysisResult result;
                    if (string.IsNullOrWhiteSpace(item.Xml))
                    {
                        result = new AnalysisResult { Status = AnalysisStatus.Incomplete, Surface = AnalysisSurface.OfflineProfile, Table = item.EntityName };
                        result.SelectedRuleIds.AddRange(selectedRules);
                        result.SkippedRuleIds.AddRange(selectedRules);
                        result.NotEvaluated.Add("No explicit FetchXML available; scope is " + item.Distribution +
                            ". Criteria/relationship-based items are not assumed to be broken. " + item.ReferencedViewError);
                    }
                    else
                        result = new Linter().Analyze(item.Xml, metadata, enabledRuleIds: selectedRules, surface: AnalysisSurface.OfflineProfile);
                    ApplyMetadataErrors(result, metadataErrors);
                    analysed.Add(new AnalysisEntry
                    {
                        SourceName = item.ProfileName + " / " + item.EntityName, OfflineSource = item,
                        Result = result, AnalysedAtUtc = DateTime.UtcNow
                    });
                }
                token.ThrowIfCancellationRequested();
                return analysed;
            }
            void Present(List<AnalysisEntry> analysed)
            {
                AddResults(analysed);
                status.Text = selected.Count + " offline table items reviewed | " +
                    selected.Count(i => !string.IsNullOrWhiteSpace(i.Xml)) + " explicit filters | " +
                    analysed.Count(e => e.Result.Status == AnalysisStatus.Invalid) + " invalid/unsupported | " +
                    selected.Count(i => string.IsNullOrWhiteSpace(i.Xml)) + " without explicit FetchXML. Sync benefit is not measured.";
            }
            if (RequiresMetadata(EffectiveRules(selectedRules, AnalysisSurface.OfflineProfile)))
                ExecuteWithConnection(() => Connected(reader => Analyse(reader, reader.CancellationToken, null), Present, "Analysing offline filters"));
            else Run((token, progress) => Analyse(null, token, progress), Present, "Analysing offline filters");
        }

        private void OpenOfflineXml()
        {
            if (offlineItems.CheckedItems.Count != 1) return;
            var item = (OfflineProfileItem)offlineItems.CheckedItems[0];
            if (string.IsNullOrWhiteSpace(item.Xml)) return;
            var warning = xml.LoadXml(item.Xml);
            layout.Clear();
            includeLayout.Checked = false;
            manualSurface = AnalysisSurface.OfflineProfile;
            manualOfflineSource = item;
            manualViewSource = null;
            querySource.Text = "Source: local offline filter - " + item.EntityName + " | " + item.SourceField;
            Navigate(WorkflowPage.ManualQuery);
            status.Text = "Offline filter opened as a local copy. Offline rule scope retained; no write-back. " + warning;
        }

        private static List<string> EffectiveRules(List<string> selected, AnalysisSurface surface) =>
            selected.Where(id => surface != AnalysisSurface.OfflineProfile || RuleCatalog.SupportsOfflineProfile(id)).ToList();

        private void UpdateOfflineActions(int? count = null, int changedIndex = -1, bool nowChecked = false)
        {
            var idle = cancellation == null;
            var checkedCount = count ?? offlineItems.CheckedItems.Count;
            var checkedItem = changedIndex >= 0
                ? offlineItems.Items.Cast<OfflineProfileItem>().Where((_, index) =>
                    index == changedIndex ? nowChecked : offlineItems.GetItemChecked(index)).FirstOrDefault()
                : offlineItems.CheckedItems.Cast<OfflineProfileItem>().FirstOrDefault();
            loadOfflineItems.Enabled = idle && offlineProfiles.SelectedItem is OfflineProfile;
            analyseOffline.Enabled = idle && checkedCount > 0;
            openOffline.Enabled = idle && checkedCount == 1 && checkedItem != null && !string.IsNullOrWhiteSpace(checkedItem.Xml);
            loadGenerated.Enabled = idle && offlineItems.SelectedItem is OfflineProfileItem;
        }

        private void ClearGeneratedDefinitions()
        {
            generatedFilters.Items.Clear();
            generatedXml.Clear();
            generatedOuterXml.Clear();
            generatedSql.Clear();
            generatedState.Text = "Generated definitions have not been loaded for the highlighted table.";
        }

        private void ClearOfflineItems()
        {
            offlineItems.Items.Clear();
            offlineXml.Clear();
            offlineScope.Clear();
            referencedSql.Clear();
            offlineDefinitionState.Text = "Highlight a table to inspect its definition.";
            offlineCount.Text = "Tables not loaded for the chosen profile.";
            ClearGeneratedDefinitions();
            ClearAnalysis();
            collectionOutcomes.Clear();
            outcome.Clear();
            UpdateOfflineActions();
        }

        private void ClearOfflineState()
        {
            offlineProfiles.Items.Clear();
            ClearOfflineItems();
            if (manualSurface == AnalysisSurface.OfflineProfile)
            {
                xml.Clear();
                layout.Clear();
                querySource.Text = "Source: manual input";
                manualSurface = AnalysisSurface.OnlineQuery;
                manualOfflineSource = null;
            }
        }
    }
}
