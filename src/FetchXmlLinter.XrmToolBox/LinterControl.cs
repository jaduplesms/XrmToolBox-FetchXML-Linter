using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
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
        public override string ToString() => SourceName + " | " + Result.Findings.Count + " findings";
    }

    public sealed class LinterControl : PluginControlBase
    {
        private enum WorkflowPage { UsageKql, UsageCsv, PersonalViews, ManualQuery, Rules }
        private readonly ToolTip tooltips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 150 };
        private readonly SplitContainer workspace = Split(Orientation.Horizontal, 400);
        private double workspaceRatio = 0.6;
        private bool positioningSplitter;
        private readonly Label querySource = new Label { Dock = DockStyle.Top, AutoSize = true, Text = "Source: manual input", AccessibleName = "Query source" };
        private readonly PictureBox logo = new PictureBox
        {
            Dock = DockStyle.Left, Width = 48, Padding = new Padding(0, 0, 10, 0),
            SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "FetchXML Linter logo",
            AccessibleRole = AccessibleRole.Graphic
        };
        private readonly XmlEditor xml = new XmlEditor("FetchXML");
        private readonly XmlEditor layout = new XmlEditor("Optional layout XML");
        private readonly TextBox search = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Filter users" };
        private readonly List<UserTarget> loadedUsers = new List<UserTarget>();
        private readonly HashSet<Guid> selectedUserIds = new HashSet<Guid>();
        private readonly Label userCount = new Label { Dock = DockStyle.Fill, AutoSize = true, AccessibleName = "User counts" };
        private readonly Label userLoadState = new Label { Dock = DockStyle.Fill, AutoSize = true, Text = "Users not loaded.", AccessibleName = "User directory status" };
        private readonly Label viewCount = new Label { AutoSize = true, Padding = new Padding(0, 7, 8, 0), AccessibleName = "View counts" };
        private readonly Button loadViews;
        private readonly Button analyseViews;
        private readonly Button openView;
        private readonly Button loadUsers;
        private readonly Button loadCandidates;
        private bool filteringUsers;
        private bool usersLoaded;
        private readonly TextBox outcome = ReadableText("Collection outcomes");
        private readonly TextBox detail = ReadableText("Finding explanation and next steps");
        private readonly TextBox technical = ReadableText("Technical evidence and coverage");
        private readonly TextBox ruleDetail = ReadableText("Rule explanation and requirements");
        private readonly TextBox kql = Editor("KQL template");
        private readonly CheckBox connectedMetadata = new CheckBox { Text = "Use connected metadata", AutoSize = true };
        private readonly CheckBox includeLayout = new CheckBox { Text = "Include layout XML", AutoSize = true };
        private readonly CheckedListBox users = Picker("Users");
        private readonly CheckedListBox views = Picker("Personal views to analyse");
        private readonly CheckedListBox shortlist = Picker("Validated CSV candidates");
        private readonly ComboBox results = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Analysis results" };
        private readonly CheckedListBox rules = Picker("Enabled analysis rules");
        private readonly ListBox navigation = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 46, AccessibleName = "Task navigation" };
        private readonly DataGridView findings = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AccessibleName = "Findings" };
        private readonly Label emptyFindings = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "Run an analysis to see findings here.", AccessibleName = "Findings empty state" };
        private readonly Label summary = new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Text = "No analysis yet. Choose a task above to get started.", AccessibleName = "Analysis summary" };
        private readonly Label ruleScope = new Label { AutoSize = true, Padding = new Padding(0, 7, 12, 0), AccessibleName = "Enabled rule count" };
        private readonly Label status = new Label { Dock = DockStyle.Fill, Text = "Private alpha | Read-only review. Queries and saved views are never executed or changed.", Padding = new Padding(12), AccessibleName = "Workflow status" };
        private readonly Panel inputs = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly Dictionary<WorkflowPage, Control> pages = new Dictionary<WorkflowPage, Control>();
        private readonly CheckBox showTechnical = new CheckBox { Text = "Technical details", AutoSize = true, AccessibleName = "Show technical details" };
        private string ruleDocumentationUrl = "";
        private readonly List<Button> actions = new List<Button>();
        private readonly List<AnalysisEntry> entries = new List<AnalysisEntry>();
        private readonly List<string> collectionOutcomes = new List<string>();
        private readonly Button cancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        private CancellationTokenSource? cancellation;
        private int generation;

        public LinterControl()
        {
            Dock = DockStyle.Fill;
            Size = new Size(1100, 850);
            MinimumSize = new Size(780, 600);
            BackColor = Color.FromArgb(244, 247, 251);
            ForeColor = Color.FromArgb(31, 41, 55);
            Font = new Font("Segoe UI", 9.5f);
            using (var stream = new MemoryStream(Convert.FromBase64String(PluginImages.Big)))
            using (var image = Image.FromStream(stream))
                logo.Image = new Bitmap(image);
            kql.ReadOnly = true;
            workspace.Panel1MinSize = 170;
            workspace.Panel2MinSize = 180;
            workspace.AccessibleName = "Input and results splitter";
            workspace.SizeChanged += (_, __) => PositionWorkspace();
            workspace.SplitterMoved += (_, __) =>
            {
                if (!positioningSplitter && !workspace.Panel2Collapsed)
                    workspaceRatio = (double)workspace.SplitterDistance / Math.Max(1, workspace.Height - workspace.SplitterWidth);
            };

            var manualSplit = Split(Orientation.Vertical, 520);
            var queryPanel = new Panel { Dock = DockStyle.Fill };
            queryPanel.Controls.Add(xml);
            queryPanel.Controls.Add(querySource);
            manualSplit.Panel1.Controls.Add(Section("FetchXML query", queryPanel));
            manualSplit.Panel2.Controls.Add(Section("Layout XML (optional)", layout));
            manualSplit.Panel2Collapsed = true;
            includeLayout.CheckedChanged += (_, __) => manualSplit.Panel2Collapsed = !includeLayout.Checked;
            var manualCommands = Commands();
            manualCommands.Controls.Add(Action("Analyse query", AnalyseManual, "Review the editor's FetchXML using the enabled rules. Does not execute the query.", true));
            manualCommands.Controls.Add(Action("Open XML file...", OpenXml, "Load a local XML file into the query editor. Does not run an analysis."));
            manualCommands.Controls.Add(Action("Format XML", FormatXml, "Indent and syntax-colour the FetchXML and included Layout XML locally. Does not change saved definitions or run an analysis."));
            manualCommands.Controls.Add(connectedMetadata);
            manualCommands.Controls.Add(includeLayout);
            pages.Add(WorkflowPage.ManualQuery, Page("Analyse a query", "Paste FetchXML, open a file, or open one checked personal view. Review the XML without executing it.", manualSplit, manualCommands));

            var pickers = Split(Orientation.Vertical, 360);
            pickers.Panel1MinSize = 250;
            pickers.Panel2MinSize = 220;
            pickers.AccessibleName = "Personal view selectors";
            var userCommands = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
            userCommands.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            userCommands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            loadUsers = Action("Load users", LoadUsers, "Load the enabled Read/Write user directory with paging. Filtering the loaded list does not make server requests.");
            userCommands.Controls.Add(loadUsers, 0, 0);
            search.Margin = new Padding(0, 8, 0, 0);
            userCommands.Controls.Add(search, 1, 0);
            var userFooter = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
            userFooter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            userFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            loadViews = Action("Load Views", CollectUsers, "Retrieve personal-view definitions for all checked users, including those hidden by the local filter.", true);
            userFooter.Controls.Add(loadViews, 0, 0);
            userFooter.Controls.Add(userCount, 1, 0);
            userCount.TextAlign = ContentAlignment.MiddleLeft;
            var userPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            userPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var index = 0; index < 5; index++)
                userPanel.RowStyles.Add(new RowStyle(index == 2 ? SizeType.Percent : SizeType.AutoSize, index == 2 ? 100 : 0));
            userPanel.Controls.Add(new Label { Text = "Filter name, email or Dataverse ID", AutoSize = true }, 0, 0);
            userPanel.Controls.Add(userCommands, 0, 1);
            userPanel.Controls.Add(users, 0, 2);
            userPanel.Controls.Add(userFooter, 0, 3);
            userPanel.Controls.Add(userLoadState, 0, 4);
            foreach (Control child in userPanel.Controls) child.Margin = Padding.Empty;
            pickers.Panel1.Controls.Add(Section("1. Select users", userPanel));
            var viewCommands = Commands();
            viewCommands.Dock = DockStyle.Bottom;
            viewCommands.Controls.Add(viewCount);
            openView = Action("Open XML", OpenViewXml, "Open the one checked view's XML in Analyse a query for inspection or editing. Does not run an analysis.");
            analyseViews = Action("Analyse", AnalyseViews, "Review all checked personal views using the enabled rules. Does not execute or change saved views.", true);
            viewCommands.Controls.Add(openView);
            viewCommands.Controls.Add(analyseViews);
            var viewPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            viewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            viewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            viewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewCommands.Dock = DockStyle.Top;
            viewPanel.Controls.Add(views, 0, 0);
            viewPanel.Controls.Add(viewCommands, 0, 1);
            viewPanel.Controls.Add(new Label { Text = "Open XML: check exactly one view.", AutoSize = true }, 0, 2);
            foreach (Control child in viewPanel.Controls) child.Margin = Padding.Empty;
            pickers.Panel2.Controls.Add(Section("2. Select personal views", viewPanel));
            search.TextChanged += (_, __) => FilterUsers();
            users.ItemCheck += (_, args) =>
            {
                if (filteringUsers) return;
                var user = (UserTarget)users.Items[args.Index];
                if (args.NewValue == CheckState.Checked) selectedUserIds.Add(user.Id);
                else selectedUserIds.Remove(user.Id);
                UpdateUserCount();
                UpdateSelectionActions();
            };
            views.ItemCheck += (_, args) => UpdateSelectionActions(views.CheckedItems.Count +
                (args.NewValue == CheckState.Checked ? 1 : 0) - (args.CurrentValue == CheckState.Checked ? 1 : 0));
            views.SelectedIndexChanged += (_, __) => UpdateSelectionActions();
            tooltips.SetToolTip(userLoadState, "Directory coverage for enabled Read/Write users. Cancelled or failed loads remain explicitly incomplete.");
            userLoadState.AutoSize = false;
            userLoadState.Height = 22;
            userLoadState.AutoEllipsis = true;
            userCount.AutoSize = false;
            userCount.Height = 22;
            userCount.MaximumSize = new Size(0, 22);
            userCount.AutoEllipsis = true;
            pages.Add(WorkflowPage.PersonalViews, Page("Review personal views", "Check users and load their view definitions, or review views retrieved from the usage CSV. Lists scroll independently.", pickers, Commands()));

            var csvCommands = Commands();
            csvCommands.Controls.Add(Action("Import CSV...", ImportCsv, "Import and validate an Application Insights CSV against the current Dataverse connection. No candidates are checked automatically."));
            loadCandidates = Action("Load matching views", CollectCandidates, "Retrieve view definitions for checked, validated CSV candidates. Requires at least one checked candidate.", true);
            csvCommands.Controls.Add(loadCandidates);
            shortlist.ItemCheck += (_, args) => UpdateSelectionActions(checkedCandidates: shortlist.CheckedItems.Count +
                (args.NewValue == CheckState.Checked ? 1 : 0) - (args.CurrentValue == CheckState.Checked ? 1 : 0));

            var templates = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "KQL template" };
            templates.Items.AddRange(new object[] { "exact-view-markers.kql", "user-table-candidates.kql" });
            templates.SelectedIndexChanged += (_, __) => LoadKql(templates.SelectedItem!.ToString()!);
            var telemetryCommands = Commands();
            telemetryCommands.Controls.Add(templates);
            telemetryCommands.Controls.Add(Action("Copy KQL", () =>
            {
                Clipboard.SetText(kql.Text);
                status.Text = "KQL copied. Run it externally in the intended Application Insights resource, then export CSV.";
            }, "Copy the selected KQL template. Run it yourself in Application Insights Logs, then export the results as CSV."));
            var telemetry = new Panel { Dock = DockStyle.Fill };
            telemetry.Controls.Add(kql);
            telemetry.Controls.Add(telemetryCommands);
            templates.SelectedIndex = 0;
            pages.Add(WorkflowPage.UsageKql, Page("Prepare usage KQL", "1. Copy a template. 2. Run it externally in the intended Application Insights resource. 3. Export CSV, then choose Import usage CSV. You can also start directly with personal views or manual XML.", Section("KQL - run externally in Application Insights Logs", telemetry), Commands()));
            pages.Add(WorkflowPage.UsageCsv, Page("Import usage CSV", "Import CSV, check validated candidates, then load matching views. The tool opens Review personal views with the retrieved definitions.", Section("Check imported candidates to enable Load matching views", shortlist), csvCommands));

            var ruleSplit = Split(Orientation.Vertical, 360);
            ruleSplit.Panel1.Controls.Add(Section("Rules included in the next analysis", rules));
            ruleSplit.Panel2.Controls.Add(Section("About this rule", ruleDetail));
            var ruleCommands = Commands();
            ruleCommands.Controls.Add(Action("Select all", () => SetRules(true), "Enable every rule for subsequent analyses. Existing results keep their original rule selection."));
            ruleCommands.Controls.Add(Action("Clear all", () => SetRules(false), "Clear the enabled rules. At least one must be checked before analysing."));
            ruleCommands.Controls.Add(Action("Microsoft guidance", () => OpenDocumentation(ruleDocumentationUrl), "Open the selected rule's Microsoft documentation in your browser."));
            rules.SelectedIndexChanged += (_, __) => ShowRule();
            rules.ItemCheck += (_, args) => UpdateRuleScope(rules.CheckedItems.Count +
                (args.NewValue == CheckState.Checked ? 1 : 0) - (args.CurrentValue == CheckState.Checked ? 1 : 0));
            foreach (var rule in RuleCatalog.All) rules.Items.Add(rule, true);
            rules.SelectedIndex = 0;
            pages.Add(WorkflowPage.Rules, Page("Analysis rules", "All rules are enabled by default. Your selection applies to every workflow for this tool session; parsing and safety checks always run.", ruleSplit, ruleCommands));
            foreach (var page in pages.Values) { inputs.Controls.Add(page); page.Visible = false; }

            navigation.Items.AddRange(new object[] { "1. Prepare usage KQL", "2. Import usage CSV", "3. Review personal views", "4. Analyse a query", "Settings: Analysis rules" });
            navigation.DrawItem += (_, args) =>
            {
                if (args.Index < 0) return;
                var selected = (args.State & DrawItemState.Selected) != 0;
                using (var background = new SolidBrush(selected ? Color.FromArgb(224, 238, 251) : BackColor))
                    args.Graphics.FillRectangle(background, args.Bounds);
                TextRenderer.DrawText(args.Graphics, navigation.Items[args.Index].ToString(), Font,
                    new Rectangle(args.Bounds.X + 12, args.Bounds.Y, args.Bounds.Width - 16, args.Bounds.Height),
                    selected ? Color.FromArgb(15, 108, 189) : ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            navigation.BackColor = BackColor;
            navigation.SelectedIndexChanged += (_, __) =>
            {
                foreach (var page in pages) page.Value.Visible = (int)page.Key == navigation.SelectedIndex;
                workspace.Panel2Collapsed = navigation.SelectedIndex == (int)WorkflowPage.Rules ||
                    navigation.SelectedIndex == (int)WorkflowPage.UsageKql;
                PositionWorkspace();
            };

            ConfigureFindingsGrid();
            findings.SelectionChanged += (_, __) => ShowFinding();
            results.SelectedIndexChanged += (_, __) => ShowResult();
            var review = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            review.Controls.Add(detail);
            var guidance = Commands();
            guidance.Dock = DockStyle.Bottom;
            guidance.Controls.Add(Action("Open finding guidance", () =>
            {
                if (SelectedFinding() is Finding finding) OpenDocumentation(finding.DocumentationUrl);
                else status.Text = "Select a finding to open its Microsoft guidance.";
            }, "Open Microsoft documentation for the selected finding in your browser."));
            review.Controls.Add(guidance);
            var findingList = new Panel { Dock = DockStyle.Fill };
            findingList.Controls.Add(findings);
            findingList.Controls.Add(emptyFindings);
            var output = Split(Orientation.Vertical, 550);
            output.Panel1MinSize = 250;
            output.Panel2MinSize = 220;
            var findingsSection = Section("Findings", findingList);
            var explanationSection = Section("Explanation and next steps", review);
            findingsSection.Padding = explanationSection.Padding = new Padding(4);
            output.Panel1.Controls.Add(findingsSection);
            output.Panel2.Controls.Add(explanationSection);
            var technicalSplit = Split(Orientation.Vertical, 460);
            technicalSplit.Panel1.Controls.Add(Section("Evidence and coverage", technical));
            technicalSplit.Panel2.Controls.Add(Section("Collection log", outcome));
            technicalSplit.Visible = false;
            var resultCommands = Commands();
            resultCommands.Controls.Add(new Label { Text = "Results for", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            resultCommands.Controls.Add(results);
            resultCommands.Controls.Add(Action("Export JSON...", ExportJson, "Save all current analysis results, coverage, query shape and provenance as JSON. Review sensitive data before sharing."));
            resultCommands.Controls.Add(Action("Export CSV...", ExportCsv, "Save findings from all current analysis results as CSV. JSON includes fuller coverage and provenance."));
            foreach (var button in resultCommands.Controls.OfType<Button>())
                button.Padding = new Padding(5, 1, 5, 1);
            var summaryRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1 };
            summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            summaryRow.Controls.Add(summary, 0, 0);
            summaryRow.Controls.Add(showTechnical, 1, 0);
            var resultLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(4), RowCount = 4, ColumnCount = 1 };
            resultLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            resultLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            resultLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            resultLayout.Controls.Add(resultCommands, 0, 0);
            resultLayout.Controls.Add(summaryRow, 0, 1);
            resultLayout.Controls.Add(output, 0, 2);
            resultLayout.Controls.Add(technicalSplit, 0, 3);
            foreach (Control child in resultLayout.Controls) child.Margin = Padding.Empty;
            resultLayout.SizeChanged += (_, __) => UpdateSummary();
            showTechnical.CheckedChanged += (_, __) =>
            {
                technicalSplit.Visible = showTechnical.Checked;
                resultLayout.RowStyles[3].Height = showTechnical.Checked ? 140 : 0;
                resultLayout.RowStyles[2].SizeType = showTechnical.Checked ? SizeType.Absolute : SizeType.Percent;
                resultLayout.RowStyles[2].Height = showTechnical.Checked ? 140 : 100;
            };
            workspace.Panel1.Controls.Add(inputs);
            workspace.Panel2.Controls.Add(resultLayout);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(navigation, 0, 0);
            body.Controls.Add(workspace, 1, 0);
            var header = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 4), BackColor = Color.White };
            header.Controls.Add(new Label { Text = "FetchXML Linter", Dock = DockStyle.Left, Width = 260, Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
            header.Controls.Add(logo);
            var common = Commands();
            common.Dock = DockStyle.Right;
            common.WrapContents = false;
            common.Controls.Add(ruleScope);
            common.Controls.Add(Action("Change rules", () => Navigate(WorkflowPage.Rules), "Choose which rules apply to the next analysis. Does not alter existing results."));
            common.Controls.Add(cancel);
            header.Controls.Add(common);
            var rootLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            rootLayout.Controls.Add(header, 0, 0);
            rootLayout.Controls.Add(body, 0, 1);
            rootLayout.Controls.Add(status, 0, 2);
            cancel.Click += (_, __) => RequestCancellation();
            cancel.AccessibleName = "Cancel operation";
            cancel.AccessibleDescription = "Request cancellation. An in-flight Dataverse read may need to finish before the operation stops.";
            tooltips.SetToolTip(cancel, cancel.AccessibleDescription);
            tooltips.SetToolTip(connectedMetadata, "Read column metadata from the connected Dataverse organization for rules that require it.");
            tooltips.SetToolTip(includeLayout, "Include the optional layout XML in this analysis.");
            tooltips.SetToolTip(showTechnical, "Show complete query shape, evidence, coverage gaps and collection outcomes.");
            Controls.Add(rootLayout);
            UpdateRuleScope(rules.CheckedItems.Count);
            UpdateUserCount();
            UpdateSelectionActions();
            Navigate(WorkflowPage.UsageKql);
        }

        private void Navigate(WorkflowPage page) => navigation.SelectedIndex = (int)page;

        private void PositionWorkspace()
        {
            if (workspace.Panel2Collapsed) return;
            var available = workspace.Height - workspace.SplitterWidth;
            if (available < workspace.Panel1MinSize + workspace.Panel2MinSize) return;
            positioningSplitter = true;
            try
            {
                workspace.SplitterDistance = Math.Max(workspace.Panel1MinSize,
                    Math.Min(available - workspace.Panel2MinSize, (int)Math.Round(available * workspaceRatio)));
            }
            finally { positioningSplitter = false; }
        }

        private static TextBox ReadableText(string name) => new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            WordWrap = true, BorderStyle = BorderStyle.None, BackColor = Color.White, AccessibleName = name
        };

        private static SplitContainer Split(Orientation orientation, int distance) => new SplitContainer
        {
            Size = new Size(1100, 700), Dock = DockStyle.Fill, Orientation = orientation,
            SplitterDistance = distance, SplitterWidth = 6, BackColor = Color.FromArgb(244, 247, 251)
        };

        private static GroupBox Section(string title, Control content)
        {
            var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Color.White };
            group.Controls.Add(content);
            return group;
        }

        private static Control Page(string title, string description, Control content, Control commands)
        {
            var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), BackColor = Color.White };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var heading = new Panel { Dock = DockStyle.Fill };
            var descriptionLabel = new Label { Text = description, Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 0) };
            heading.Controls.Add(descriptionLabel);
            heading.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 14, FontStyle.Bold) });
            page.Controls.Add(heading, 0, 0);
            page.Controls.Add(commands, 0, 1);
            page.Controls.Add(content, 0, 2);
            page.SizeChanged += (_, __) =>
            {
                var compact = page.Height < 360;
                heading.Visible = !compact;
                descriptionLabel.Visible = !compact;
                page.RowStyles[0].Height = compact ? 0 : 70;
            };
            return page;
        }

        private void ConfigureFindingsGrid()
        {
            findings.EnableHeadersVisualStyles = false;
            findings.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 242, 250);
            findings.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
            findings.ColumnHeadersHeight = 34;
            findings.RowTemplate.Height = 34;
            findings.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 238, 251);
            findings.DefaultCellStyle.SelectionForeColor = ForeColor;
            findings.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            findings.GridColor = Color.FromArgb(230, 235, 241);
            findings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity", Width = 85 });
            findings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rule", HeaderText = "Rule", Width = 75 });
            findings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Finding", HeaderText = "Finding", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 130 });
            findings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Location", HeaderText = "Location", Width = 85 });
        }

        private void SetRules(bool enabled)
        {
            for (var index = 0; index < rules.Items.Count; index++) rules.SetItemChecked(index, enabled);
        }

        private void UpdateRuleScope(int count) => ruleScope.Text = count + " of " + RuleCatalog.All.Count + " rules enabled";

        private void ShowRule()
        {
            if (!(rules.SelectedItem is RuleDefinition rule)) return;
            ruleDocumentationUrl = rule.DocumentationUrl;
            ruleDetail.Text = rule.Name + " (" + rule.Id + ") | " + rule.Group + Environment.NewLine + Environment.NewLine +
                rule.Description + Environment.NewLine + Environment.NewLine + "Requirements" + Environment.NewLine +
                rule.Requirements + Environment.NewLine + Environment.NewLine + "Limitations" + Environment.NewLine + rule.Limitation;
        }

        private static void OpenDocumentation(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("Select a rule or finding first.");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private List<string> SelectedRules() => rules.CheckedItems.Cast<RuleDefinition>().Select(r => r.Id).ToList();

        private bool CheckRuleSelection(List<string> selected)
        {
            if (selected.Count > 0) return true;
            status.Text = "Select at least one analysis rule before running an analysis.";
            Navigate(WorkflowPage.Rules);
            return false;
        }

        private static bool RequiresMetadata(List<string> selected) =>
            RuleCatalog.All.Any(r => r.RequiresMetadata && selected.Contains(r.Id));

        private static TextBox Editor(string name) => new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, AcceptsTab = true, MaxLength = Linter.MaximumXmlCharacters,
            Font = new Font("Consolas", 10), AccessibleName = name
        };

        private static CheckedListBox Picker(string name) => new CheckedListBox
        {
            Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true, IntegralHeight = false, AccessibleName = name
        };

        private static FlowLayoutPanel Commands() => new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0), WrapContents = true
        };

        private Button Action(string text, System.Action handler, string tooltip, bool primary = false)
        {
            var button = new Button
            {
                Text = text, AutoSize = true, AccessibleName = text, FlatStyle = FlatStyle.Flat,
                Padding = new Padding(7, 4, 7, 4), Margin = new Padding(0, 2, 8, 2),
                BackColor = primary ? Color.FromArgb(15, 108, 189) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(31, 41, 55)
            };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(15, 108, 189) : Color.FromArgb(205, 215, 225);
            tooltips.SetToolTip(button, tooltip);
            button.AccessibleDescription = tooltip;
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
                {
                    var warning = xml.LoadXml(ReadBounded(dialog.FileName, Linter.MaximumXmlCharacters));
                    querySource.Text = "Source: " + Path.GetFileName(dialog.FileName);
                    status.Text = warning.Length == 0 ? "Opened and formatted XML locally. Click Analyse query to review it." : warning;
                }
        }

        private void FormatXml()
        {
            var formattedQuery = XmlEditor.Format(xml.Text);
            var formattedLayout = includeLayout.Checked ? XmlEditor.Format(layout.Text) : null;
            xml.SetFormattedXml(formattedQuery);
            if (formattedLayout != null) layout.SetFormattedXml(formattedLayout);
            status.Text = "XML indented and syntax-coloured locally. Saved definitions are unchanged; click Analyse query to review the editor contents.";
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
            var selectedRules = SelectedRules();
            if (!CheckRuleSelection(selectedRules)) return;
            collectionOutcomes.Clear();
            outcome.Clear();
            var source = xml.Text;
            var suppliedLayout = includeLayout.Checked ? layout.Text : "";
            if (connectedMetadata.Checked && RequiresMetadata(selectedRules))
            {
                ExecuteWithConnection(() => Connected(reader =>
                {
                    var first = new Linter().Analyze(source, layoutXml: suppliedLayout, enabledRuleIds: selectedRules);
                    if (first.Status == AnalysisStatus.Invalid)
                        return new AnalysisEntry { SourceName = "Manual FetchXML", Result = first, AnalysedAtUtc = DateTime.UtcNow };
                    var errors = new List<string>();
                    var metadata = reader.GetMetadata(first.Tables, errors);
                    var result = new Linter().Analyze(source, metadata, suppliedLayout, selectedRules);
                    ApplyMetadataErrors(result, errors);
                    return new AnalysisEntry { SourceName = "Manual FetchXML", Result = result, AnalysedAtUtc = DateTime.UtcNow };
                }, entry => AddResults(new[] { entry }), "Analysing query"));
            }
            else
                Run((token, progress) =>
                {
                    token.ThrowIfCancellationRequested();
                    var result = new Linter().Analyze(source, layoutXml: suppliedLayout, enabledRuleIds: selectedRules);
                    token.ThrowIfCancellationRequested();
                    return new AnalysisEntry { SourceName = "Manual FetchXML", Result = result, AnalysedAtUtc = DateTime.UtcNow };
                }, entry => AddResults(new[] { entry }), "Analysing query");
        }

        private void LoadUsers()
        {
            ExecuteWithConnection(() => Connected(reader => reader.LoadUsers(), PresentUsers, usersLoaded ? "Refreshing users" : "Loading users"));
        }

        private void PresentUsers(UserLoadResult result)
        {
            loadedUsers.Clear();
            loadedUsers.AddRange(result.Users);
            usersLoaded = true;
            selectedUserIds.IntersectWith(loadedUsers.Select(u => u.Id));
            userLoadState.Text = result.Complete ? "Directory complete (enabled Read/Write users)." : result.Outcome;
            FilterUsers();
            status.Text = result.Outcome;
            UpdateSelectionActions();
        }

        private void FilterUsers()
        {
            var term = search.Text.Trim();
            filteringUsers = true;
            users.BeginUpdate();
            try
            {
                users.Items.Clear();
                foreach (var user in loadedUsers.Where(u => term.Length == 0 ||
                    u.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    u.Email.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    u.Id.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                    users.Items.Add(user, selectedUserIds.Contains(user.Id));
            }
            finally { users.EndUpdate(); filteringUsers = false; }
            UpdateUserCount();
            UpdateSelectionActions();
        }

        private void UpdateUserCount()
        {
            userCount.Text = selectedUserIds.Count + " selected | " + loadedUsers.Count + " loaded | " + users.Items.Count + " shown";
            tooltips.SetToolTip(userCount, userCount.Text);
        }

        private void UpdateSelectionActions(int? checkedViews = null, int? checkedCandidates = null)
        {
            var busy = cancellation != null;
            loadViews.Enabled = !busy && selectedUserIds.Count > 0;
            loadUsers.Enabled = !busy;
            loadUsers.Text = usersLoaded ? "Refresh users" : "Load users";
            loadUsers.AccessibleName = loadUsers.Text;
            loadUsers.AccessibleDescription = usersLoaded
                ? "Reload the enabled Read/Write user directory. Checked users still present are retained, including hidden selections."
                : "Load the enabled Read/Write user directory with paging. Filtering the loaded list does not make server requests.";
            tooltips.SetToolTip(loadUsers, loadUsers.AccessibleDescription);
            var count = checkedViews ?? views.CheckedItems.Count;
            openView.Enabled = !busy && count == 1;
            analyseViews.Enabled = !busy && count > 0;
            viewCount.Text = count + " selected";
            loadCandidates.Enabled = !busy && (checkedCandidates ?? shortlist.CheckedItems.Count) > 0;
        }

        private void CollectUsers()
        {
            var selected = loadedUsers.Where(u => selectedUserIds.Contains(u.Id)).ToList();
            if (selected.Count == 0) { status.Text = "Select at least one user to load views."; return; }
            ExecuteWithConnection(() => Connected(reader => reader.Collect(selected), PresentCollection, "Loading personal views"));
        }

        private void PresentCollection(CollectionResult collected)
        {
            views.Items.Clear();
            views.Items.AddRange(collected.Views.Cast<object>().ToArray());
            UpdateSelectionActions();
            collectionOutcomes.Clear();
            collectionOutcomes.AddRange(collected.Outcomes);
            outcome.Text = string.Join(Environment.NewLine, collectionOutcomes);
            ClearAnalysis();
            Navigate(WorkflowPage.PersonalViews);
            status.Text = (collected.Cancelled ? "Cancelled; partial collection. " : "") +
                collected.Views.Count + " personal views loaded. Select views to analyse; collection details are available in the technical log.";
        }

        private void AnalyseViews()
        {
            var selectedRules = SelectedRules();
            if (!CheckRuleSelection(selectedRules)) return;
            var selected = views.CheckedItems.Cast<ViewSource>().ToList();
            if (selected.Count == 0) { status.Text = "Select at least one loaded personal view to analyse."; return; }
            if (!RequiresMetadata(selectedRules))
            {
                Run((token, progress) => selected.Select((v, index) =>
                {
                    token.ThrowIfCancellationRequested();
                    progress.Report("Analysing view " + (index + 1) + " of " + selected.Count + ": " + v.Name);
                    var result = new Linter().Analyze(v.Xml, layoutXml: v.Layout, enabledRuleIds: selectedRules);
                    token.ThrowIfCancellationRequested();
                    return new AnalysisEntry { SourceName = v.Name, Source = v, Result = result, AnalysedAtUtc = DateTime.UtcNow };
                }).ToList(), AddResults, "Analysing personal views");
                return;
            }
            ExecuteWithConnection(() => Connected(reader =>
            {
                var linter = new Linter();
                var tables = selected.SelectMany(v => linter.Analyze(v.Xml, enabledRuleIds: selectedRules).Tables).Distinct().ToList();
                var errors = new List<string>();
                var metadata = reader.GetMetadata(tables, errors);
                return selected.Select(v =>
                {
                    var result = linter.Analyze(v.Xml, metadata, v.Layout, selectedRules);
                    ApplyMetadataErrors(result, errors);
                    return new AnalysisEntry { SourceName = v.Name, Source = v, Result = result, AnalysedAtUtc = DateTime.UtcNow };
                }).ToList();
            }, AddResults, "Analysing personal views"));
        }

        private static void ApplyMetadataErrors(AnalysisResult result, List<string> errors)
        {
            if (errors.Count == 0) return;
            result.NotEvaluated.AddRange(errors);
            if (result.Status != AnalysisStatus.Invalid) result.Status = AnalysisStatus.Incomplete;
        }

        private void OpenViewXml()
        {
            if (views.CheckedItems.Count != 1)
            {
                status.Text = "Check exactly one personal view to open its XML.";
                return;
            }
            var source = (ViewSource)views.CheckedItems[0];
            var warnings = new[] { xml.LoadXml(source.Xml), layout.LoadXml(source.Layout) }.Where(w => w.Length > 0);
            includeLayout.Checked = !string.IsNullOrWhiteSpace(source.Layout);
            querySource.Text = "Source: personal view - " + source.Name;
            Navigate(WorkflowPage.ManualQuery);
            status.Text = "Opened '" + source.Name + "' in Analyse a query. XML loaded only; click Analyse query to review it. " +
                string.Join(" ", warnings);
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
                    outcome.Text = imported.Errors.Count == 0
                        ? "CSV validation completed. Candidates are bound to the current Dataverse connection; verify the Application Insights resource belongs to this organization."
                        : string.Join(Environment.NewLine, imported.Errors);
                    status.Text = imported.Candidates.Count + " valid candidate rows; " + imported.Errors.Count +
                        " rejected rows; " + imported.DuplicateCount + " exact duplicates removed. Nothing is selected automatically.";
                    UpdateSelectionActions();
                }, "Importing usage CSV"));
            }
        }

        private void CollectCandidates()
        {
            var candidates = shortlist.CheckedItems.Cast<Candidate>().ToList();
            if (candidates.Count == 0) { status.Text = "Import CSV and check validated candidate rows first."; return; }
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
            }, PresentCollection, "Loading matching personal views"));
        }

        internal void ExecuteWithConnection(System.Action action) =>
            ExecuteMethod(callback => callback.ExternalAction(), new ExternalMethodCallerInfo(action));

        private void Connected<T>(Func<DataverseReader, T> work, Action<T> apply, string operation)
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
            }, apply, operation);
        }

        private void RequestCancellation()
        {
            cancellation?.Cancel();
            status.Text = "Cancellation requested; an in-flight Dataverse read may need to finish.";
        }

        internal async void Run<T>(Func<CancellationToken, IProgress<string>, T> work, Action<T> apply, string operation)
        {
            if (cancellation != null) { status.Text = "Wait for the active operation to finish."; return; }
            var current = new CancellationTokenSource();
            cancellation = current;
            var startedGeneration = generation;
            foreach (var action in actions) action.Enabled = false;
            rules.Enabled = false;
            connectedMetadata.Enabled = false;
            includeLayout.Enabled = false;
            navigation.Enabled = false;
            cancel.Enabled = true;
            status.Text = operation + "...";
            Task<T>? task = null;
            var finished = false;
            using (var dialog = new OperationDialog(operation,
                () => (task != null && task.IsCompleted) || IsDisposed || generation != startedGeneration, () =>
            {
                current.Cancel();
                if (!IsDisposed) status.Text = "Cancellation requested; an in-flight Dataverse read may need to finish.";
            }))
            {
                var progress = new Progress<string>(message =>
                {
                    if (!finished && !IsDisposed && generation == startedGeneration && !current.IsCancellationRequested)
                    {
                        status.Text = message;
                        dialog.Report(message);
                    }
                });
                try
                {
                    task = Task.Run(() => work(current.Token, progress));
                    await Task.WhenAny(task, Task.Delay(250));
                    if (!task.IsCompleted && !IsDisposed && generation == startedGeneration) dialog.ShowDialog(this);
                    var value = await task;
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
                    finished = true;
                    cancellation = null;
                    current.Dispose();
                    if (!IsDisposed)
                    {
                        foreach (var action in actions) action.Enabled = true;
                        rules.Enabled = true;
                        connectedMetadata.Enabled = true;
                        includeLayout.Enabled = true;
                        navigation.Enabled = true;
                        cancel.Enabled = false;
                        UpdateSelectionActions();
                    }
                }
            }
        }

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            generation++;
            cancellation?.Cancel();
            loadedUsers.Clear();
            selectedUserIds.Clear();
            usersLoaded = false;
            userLoadState.Text = "Users not loaded.";
            search.Clear();
            users.Items.Clear();
            views.Items.Clear();
            shortlist.Items.Clear();
            ClearAnalysis();
            collectionOutcomes.Clear();
            outcome.Clear();
            UpdateUserCount();
            UpdateSelectionActions();
            base.UpdateConnection(newService, detail, actionName, parameter);
            if (actionName != "Recaller")
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

        private void ClearAnalysis()
        {
            entries.Clear();
            results.Items.Clear();
            findings.Rows.Clear();
            detail.Clear();
            technical.Clear();
            summary.Text = "No analysis yet. Select a query or personal views to review.";
            emptyFindings.Text = "Run an analysis to see findings here.";
            emptyFindings.Visible = true;
            findings.Visible = false;
        }

        private void ShowResult()
        {
            if (!(results.SelectedItem is AnalysisEntry entry)) return;
            var result = entry.Result;
            UpdateSummary();
            findings.Rows.Clear();
            foreach (var finding in result.Findings)
            {
                var rule = RuleCatalog.All.Single(r => r.Id == finding.RuleId);
                var index = findings.Rows.Add(finding.Severity == Severity.Information ? "Review" : finding.Severity.ToString(),
                    finding.RuleId, rule.Name, "Line " + finding.Line);
                findings.Rows[index].Tag = finding;
            }
            findings.Visible = result.Findings.Count > 0;
            emptyFindings.Visible = !findings.Visible;
            emptyFindings.Text = result.Status == AnalysisStatus.Invalid ? result.Error :
                "No findings from the selected rules." + Environment.NewLine + Environment.NewLine +
                (result.NotEvaluated.Count > 0 ? "Some checks were unavailable. Review the coverage gaps before drawing conclusions." :
                    "This is a static review, not proof of query performance.");
            var text = new StringBuilder(entry.SourceName + Environment.NewLine + "Status: " + result.Status +
                Environment.NewLine + "Selected rules: " + string.Join(", ", result.SelectedRuleIds) +
                Environment.NewLine + "Engine: " + result.EngineVersion +
                Environment.NewLine + "SHA-256: " + result.SourceHash + Environment.NewLine);
            if (result.Shape is QueryShape shape)
                text.AppendLine("Shape: " + shape.ExplicitColumns + " explicit columns; " + shape.Joins + " joins (depth " +
                    shape.MaximumJoinDepth + "); " + shape.SortColumns + " sorts; " + shape.InnerJoins + " inner / " + shape.OuterJoins + " outer / " + shape.OtherJoins +
                    " specialized joins; all-column selections=" + shape.AllColumnSelections + "; distinct=" + shape.Distinct)
                    .AppendLine("Explicit projection metadata: lookups=" + (shape.LookupColumns?.ToString() ?? "unknown") +
                        "; logical=" + (shape.LogicalColumns?.ToString() ?? "unknown") +
                        "; calculated/formula=" + (shape.CalculatedColumns?.ToString() ?? "unknown"));
            if (result.Error.Length > 0) text.AppendLine(result.Error);
            foreach (var finding in entry.Result.Findings)
                text.AppendLine().AppendLine(finding.RuleId + " | " + finding.Category + " | " + finding.Severity)
                    .AppendLine(finding.Location + " (line " + finding.Line + ")")
                    .AppendLine("Evidence: " + finding.Evidence);
            foreach (var missing in entry.Result.NotEvaluated) text.AppendLine("NOT EVALUATED: " + missing);
            if (entry.Source?.Candidate != null)
            {
                var candidate = entry.Source.Candidate;
                text.AppendLine().AppendLine("Imported candidate: " + candidate.Attribution +
                    "; " + candidate.MetricKind + " p95=" + candidate.P95Ms +
                    " ms; samples=" + candidate.SampleCount)
                    .AppendLine("Observed duration is not a proven SQL execution time or a measured optimization benefit.");
            }
            technical.Text = text.ToString();
            if (findings.Rows.Count > 0)
            {
                findings.ClearSelection();
                findings.Rows[0].Selected = true;
                ShowFinding();
            }
            else detail.Text = emptyFindings.Text;
        }

        private void UpdateSummary()
        {
            if (!(results.SelectedItem is AnalysisEntry entry)) return;
            var result = entry.Result;
            var compact = workspace.Panel2.Height < 290;
            var coverage = result.Status == AnalysisStatus.Invalid ? "Cannot analyse this query" :
                result.Status == AnalysisStatus.Incomplete ? "Partial review - some checks could not be completed" : "Selected checks completed";
            summary.Padding = compact ? new Padding(4, 0, 4, 0) : new Padding(8, 4, 8, 4);
            summary.Text = compact
                ? (result.Status == AnalysisStatus.Invalid ? "Cannot analyse this query" : result.Status == AnalysisStatus.Incomplete ? "Partial review" : "Selected checks completed") +
                    " | " + result.Findings.Count + " findings | " + result.NotEvaluated.Count + " coverage gaps"
                : coverage + Environment.NewLine +
                result.Findings.Count(f => f.Severity == Severity.Warning || f.Severity == Severity.Error) + " warnings/errors | " +
                result.Findings.Count(f => f.Severity == Severity.Information) + " review notes | " +
                result.NotEvaluated.Count + " coverage gaps | " + result.SelectedRuleIds.Count + " of " + RuleCatalog.All.Count + " rules selected" +
                (compact || result.Shape == null ? "" : Environment.NewLine + "Shape: " + result.Shape.ExplicitColumns +
                    " explicit columns | " + result.Shape.Joins + " joins (depth " + result.Shape.MaximumJoinDepth +
                    ") | " + result.Shape.SortColumns + " sorts") +
                (!compact && result.NotEvaluated.Count > 0 ? Environment.NewLine + "Coverage: " + result.NotEvaluated[0] +
                    (result.NotEvaluated.Count > 1 ? " (more in technical details)" : "") : "");
            summary.ForeColor = result.Status == AnalysisStatus.Invalid ? Color.FromArgb(164, 38, 44) : ForeColor;
        }

        private Finding? SelectedFinding() => findings.SelectedRows.Count > 0 ? findings.SelectedRows[0].Tag as Finding : null;

        private void ShowFinding()
        {
            var finding = SelectedFinding();
            if (finding == null) { detail.Text = "Select a finding to read its explanation and suggested action."; return; }
            var rule = RuleCatalog.All.Single(r => r.Id == finding.RuleId);
            detail.Text = rule.Name + " (" + finding.RuleId + ")" + Environment.NewLine + Environment.NewLine +
                finding.Explanation + Environment.NewLine + Environment.NewLine +
                "Suggested action" + Environment.NewLine + finding.Recommendation + Environment.NewLine + Environment.NewLine +
                "Important limitations" + Environment.NewLine + finding.Limitation;
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
                var output = new StringBuilder("Source,Status,SourceHash,SelectedRules,RuleId,Category,Severity,Location,Evidence,Explanation,Recommendation,Limitation,DocumentationUrl\r\n");
                foreach (var entry in entries)
                {
                    if (entry.Result.Findings.Count == 0)
                        output.AppendLine(string.Join(",", new[] { entry.SourceName, entry.Result.Status.ToString(), entry.Result.SourceHash, string.Join(";", entry.Result.SelectedRuleIds),
                            "", "", "", "", entry.Result.Error, "", "", string.Join("; ", entry.Result.NotEvaluated), "" }.Select(ShortlistCsv.EncodeCell)));
                    foreach (var finding in entry.Result.Findings)
                        output.AppendLine(string.Join(",", new[] { entry.SourceName, entry.Result.Status.ToString(), entry.Result.SourceHash, string.Join(";", entry.Result.SelectedRuleIds),
                            finding.RuleId, finding.Category.ToString(), finding.Severity.ToString(), finding.Location, finding.Evidence,
                            finding.Explanation, finding.Recommendation, finding.Limitation, finding.DocumentationUrl }.Select(ShortlistCsv.EncodeCell)));
                }
                File.WriteAllText(dialog.FileName, output.ToString(), new UTF8Encoding(true));
                status.Text = "Findings CSV exported. JSON includes fuller coverage and telemetry provenance.";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellation?.Cancel();
                logo.Image?.Dispose();
                logo.Image = null;
                tooltips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
