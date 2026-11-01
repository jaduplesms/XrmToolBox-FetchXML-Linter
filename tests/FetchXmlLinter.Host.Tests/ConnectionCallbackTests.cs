using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using FetchXmlLinter.Core;
using FetchXmlLinter.XrmToolBox;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmToolBox.Extensibility;
using Xunit;

namespace FetchXmlLinter.Host.Tests
{
    public sealed class ConnectionCallbackTests
    {
        [Fact]
        public void SameNamedPersonalViewResultsIdentifyOwnersAndRetainThemInLocalCopies()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var views = Find<CheckedListBox>(control, "Personal views to analyse");
                    var first = new ViewSource
                    {
                        Name = "Synthetic view", OwnerId = Guid.NewGuid(), OwnerName = "Synthetic user A",
                        Xml = "<fetch><entity name='account'><filter><condition attribute='name' operator='like' value='%sample'/></filter></entity></fetch>"
                    };
                    var second = new ViewSource
                    {
                        Name = first.Name, OwnerId = Guid.NewGuid(), OwnerName = "Synthetic user B",
                        Xml = "<fetch><entity name='account'/></fetch>"
                    };
                    views.Items.Add(first, true);
                    views.Items.Add(second, true);
                    var rules = Find<CheckedListBox>(control, "Enabled analysis rules");
                    for (var index = 0; index < rules.Items.Count; index++)
                        rules.SetItemChecked(index, ((RuleDefinition)rules.Items[index]).Id == "FXL002");
                    Invoke(control, "AnalyseViews");
                    WaitForIdle(control);
                    var results = Find<ComboBox>(control, "Analysis results");
                    Assert.Equal(2, results.Items.Count);
                    Assert.Equal("Synthetic view | Synthetic user A | 1 findings", results.GetItemText(results.Items[0]));
                    Assert.Equal("Synthetic view | Synthetic user B | 0 findings", results.GetItemText(results.Items[1]));
                    results.SelectedIndex = 1;
                    Assert.Same(second, ((AnalysisEntry)results.SelectedItem).Source);
                    views.SetItemChecked(1, false);
                    Invoke(control, "OpenViewXml");
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    var local = Assert.Single(results.Items.Cast<AnalysisEntry>());
                    Assert.Same(first, local.Source);
                    Assert.Equal("Local personal view - Synthetic view | Synthetic user A | 1 findings",
                        results.GetItemText(local));
                }
            });
        }

        [Fact]
        public void IntentNavigationRetainsTelemetryStepAndSharedSelectionsAndResults()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var navigation = Find<ListBox>(control, "Task navigation");
                    Assert.Equal(new[] { "Personal views", "Offline profiles", "FetchXML query", "Analysis rules" },
                        navigation.Items.Cast<string>());
                    Assert.Equal("Personal views", navigation.SelectedItem);
                    var browse = Find<RadioButton>(control, "Browse views");
                    var telemetry = Find<RadioButton>(control, "Find candidates using telemetry");
                    Assert.True(browse.Checked);
                    var views = Find<CheckedListBox>(control, "Personal views to analyse");
                    views.Items.Add(new ViewSource
                    {
                        Name = "Synthetic view", Xml = "<fetch><entity name='account'><attribute name='name'/></entity></fetch>"
                    }, true);
                    var rules = Find<CheckedListBox>(control, "Enabled analysis rules");
                    for (var index = 0; index < rules.Items.Count; index++)
                        rules.SetItemChecked(index, ((RuleDefinition)rules.Items[index]).Id == "FXL002");
                    Invoke(control, "AnalyseViews");
                    WaitForIdle(control);
                    var results = Find<ComboBox>(control, "Analysis results");
                    var entry = Assert.Single(results.Items.Cast<AnalysisEntry>());
                    Assert.Equal("Personal view", Find<System.Windows.Forms.Label>(control, "Analysis context").Text);
                    var splitter = Find<SplitContainer>(control, "Input and results splitter");
                    telemetry.Checked = true;
                    Assert.True(splitter.Panel2Collapsed);
                    Assert.Equal("Personal views", navigation.SelectedItem);
                    Find<Button>(control, "2. Import CSV").PerformClick();
                    Assert.False(splitter.Panel2Collapsed);
                    Assert.True(Find<CheckedListBox>(control, "Validated CSV candidates").Visible);
                    navigation.SelectedItem = "Offline profiles";
                    Assert.False(telemetry.Visible);
                    navigation.SelectedItem = "FetchXML query";
                    Assert.False(telemetry.Visible);
                    navigation.SelectedItem = "Analysis rules";
                    Assert.True(splitter.Panel2Collapsed);
                    navigation.SelectedItem = "Personal views";
                    Assert.True(telemetry.Checked);
                    Assert.True(Find<CheckedListBox>(control, "Validated CSV candidates").Visible);
                    Assert.False(splitter.Panel2Collapsed);
                    Find<Button>(control, "3. Review views").PerformClick();
                    Assert.True(views.Visible);
                    Assert.False(Find<CheckedListBox>(control, "Users").Visible);
                    Assert.Single(views.CheckedItems);
                    browse.Checked = true;
                    Assert.True(views.Visible);
                    Assert.True(Find<CheckedListBox>(control, "Users").Visible);
                    Assert.False(Find<Button>(control, "2. Import CSV").Visible);
                    Assert.Equal(new[] { "FXL002" }, rules.CheckedItems.Cast<RuleDefinition>().Select(r => r.Id));
                    Assert.Same(entry, results.SelectedItem);
                }
            });
        }

        [Fact]
        public void TelemetryCollectionOpensSharedViewReviewInsidePersonalViews()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    Find<RadioButton>(control, "Find candidates using telemetry").Checked = true;
                    Find<Button>(control, "2. Import CSV").PerformClick();
                    var collected = new CollectionResult();
                    var view = new ViewSource { Name = "Synthetic matching view", Xml = "<fetch><entity name='account'/></fetch>" };
                    collected.Views.Add(view);
                    Invoke(control, "PresentCollection", collected);
                    Assert.Equal("Personal views", Find<ListBox>(control, "Task navigation").SelectedItem);
                    Assert.True(Find<RadioButton>(control, "Find candidates using telemetry").Checked);
                    var views = Find<CheckedListBox>(control, "Personal views to analyse");
                    Assert.True(views.Visible);
                    Assert.Same(view, Assert.Single(views.Items.Cast<ViewSource>()));
                    Assert.Empty(views.CheckedItems);
                    Assert.False(Find<CheckedListBox>(control, "Users").Visible);
                    Assert.False(Find<SplitContainer>(control, "Input and results splitter").Panel2Collapsed);
                }
            });
        }

        [Fact]
        public void NewQueryResetsOfflineContextWithoutRelabellingExistingResults()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    const string xml = "<fetch><entity name='account'><attribute name='name'/></entity></fetch>";
                    Find<CheckedListBox>(control, "Personal views to analyse").Items.Add(
                        new ViewSource { Name = "Synthetic view", Xml = xml }, true);
                    Invoke(control, "OpenViewXml");
                    Find<CheckedListBox>(control, "Offline profile tables").Items.Add(
                        new OfflineProfileItem { EntityName = "account", ConfiguredXml = xml }, true);
                    Invoke(control, "OpenOfflineXml");
                    var navigation = Find<ListBox>(control, "Task navigation");
                    navigation.SelectedItem = "Personal views";
                    navigation.SelectedItem = "FetchXML query";
                    Assert.Contains("local offline", Find<System.Windows.Forms.Label>(control, "Query source").Text);
                    var rules = Find<CheckedListBox>(control, "Enabled analysis rules");
                    for (var index = 0; index < rules.Items.Count; index++)
                        rules.SetItemChecked(index, ((RuleDefinition)rules.Items[index]).Id == "FXL002");
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    var results = Find<ComboBox>(control, "Analysis results");
                    var offline = Assert.Single(results.Items.Cast<AnalysisEntry>());
                    Assert.Null(offline.Source);
                    Assert.Equal("Local offline filter - account | 0 findings", offline.ToString());
                    var context = Find<System.Windows.Forms.Label>(control, "Analysis context");
                    Assert.Equal("Offline profile filter", context.Text);
                    Invoke(control, "NewQuery");
                    Assert.Empty(Find<RichTextBox>(control, "FetchXML").Text);
                    Assert.Same(offline, results.SelectedItem);
                    Assert.Equal("Offline profile filter", context.Text);
                    Find<RichTextBox>(control, "FetchXML").Text = xml;
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    var supplied = Assert.Single(results.Items.Cast<AnalysisEntry>());
                    Assert.Equal(AnalysisSurface.OnlineQuery, supplied.Result.Surface);
                    Assert.Null(supplied.OfflineSource);
                    Assert.Null(supplied.Source);
                    Assert.Equal("Supplied FetchXML | 0 findings", supplied.ToString());
                    Assert.Equal("Supplied FetchXML", context.Text);
                }
            });
        }

        [Fact]
        public void OfflineCheckTargetingKeepsLocalEditorInOfflineContextAndReportsCriteriaOnlyCoverage()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var requests = 0;
                    control.OnRequestConnection += (_, __) => requests++;
                    var profiles = Find<ComboBox>(control, "Offline profiles");
                    profiles.Items.Add(new OfflineProfile { Id = Guid.NewGuid(), Name = "Synthetic profile" });
                    profiles.SelectedIndex = 0;
                    var configured = new OfflineProfileItem
                    {
                        Id = Guid.NewGuid(), ProfileName = "Synthetic profile", EntityName = "account",
                        ConfiguredXml = "<fetch><entity name='account'><all-attributes/>" +
                            "<filter><condition attribute='name' operator='like' value='%sample'/></filter></entity></fetch>"
                    };
                    var criteriaOnly = new OfflineProfileItem
                    {
                        Id = Guid.NewGuid(), EntityName = "contact", DistributionCriteria = 0,
                        ViewQuery = "select contactid from contact", SelectedColumns = "[\"fullname\"]"
                    };
                    Invoke(control, "PresentOfflineItems", new List<OfflineProfileItem> { configured, criteriaOnly });
                    var items = Find<CheckedListBox>(control, "Offline profile tables");
                    var open = Find<Button>(control, "Open filter XML");
                    var analyse = Find<Button>(control, "Analyse tables");
                    Assert.False(open.Enabled);
                    Assert.False(analyse.Enabled);
                    items.SetItemChecked(0, true);
                    items.SelectedIndex = 1;
                    Assert.True(open.Enabled);
                    Assert.Empty(Find<RichTextBox>(control, "Configured offline FetchXML").Text);
                    Assert.Contains("fullname", Find<TextBox>(control, "Offline scope and columns").Text);
                    Assert.Equal(criteriaOnly.ViewQuery, Find<TextBox>(control, "Referenced view SQL").Text);
                    Invoke(control, "OpenOfflineXml");
                    Assert.Contains("account", Find<RichTextBox>(control, "FetchXML").Text);
                    Assert.Contains("local offline", Find<System.Windows.Forms.Label>(control, "Query source").Text);
                    var rules = Find<CheckedListBox>(control, "Enabled analysis rules");
                    for (var index = 0; index < rules.Items.Count; index++)
                    {
                        var id = ((RuleDefinition)rules.Items[index]).Id;
                        rules.SetItemChecked(index, id == "FXL001" || id == "FXL002" || id == "FXL005");
                    }
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    var results = Find<ComboBox>(control, "Analysis results");
                    var manual = Assert.IsType<AnalysisEntry>(Assert.Single(results.Items.Cast<object>()));
                    Assert.Same(configured, manual.OfflineSource);
                    Assert.Equal("Offline profile filter", Find<System.Windows.Forms.Label>(control, "Analysis context").Text);
                    Assert.Equal(AnalysisSurface.OfflineProfile, manual.Result.Surface);
                    Assert.Equal(new[] { "FXL002" }, manual.Result.Findings.Select(f => f.RuleId));
                    Assert.Contains("FXL005", manual.Result.SkippedRuleIds);
                    items.SetItemChecked(1, true);
                    Assert.False(open.Enabled);
                    Invoke(control, "AnalyseOfflineItems");
                    WaitForIdle(control);
                    Assert.True(analyse.Enabled);
                    var entries = results.Items.Cast<AnalysisEntry>().ToList();
                    Assert.Equal(2, entries.Count);
                    Assert.All(entries, e => Assert.Equal(AnalysisSurface.OfflineProfile, e.Result.Surface));
                    Assert.Equal(AnalysisStatus.Incomplete, entries[1].Result.Status);
                    Assert.Contains(entries[1].Result.NotEvaluated, s => s.Contains("No explicit FetchXML"));
                    Assert.Equal(0, requests);
                    items.SetItemChecked(0, false);
                    Assert.False(open.Enabled);
                }
            });
        }

        [Fact]
        public void GeneratedSqlAndPlaceholderPayloadsAreReadOnlyAndClearedOnSelectionOrConnectionChanges()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var profile = new OfflineProfile { Id = Guid.NewGuid(), Name = "Synthetic profile" };
                    Invoke(control, "PresentOfflineProfiles", new List<OfflineProfile> { profile });
                    var profiles = Find<ComboBox>(control, "Offline profiles");
                    profiles.SelectedIndex = 0;
                    Invoke(control, "PresentOfflineItems", new List<OfflineProfileItem>
                    {
                        new OfflineProfileItem { EntityName = "account" }, new OfflineProfileItem { EntityName = "contact" }
                    });
                    var items = Find<CheckedListBox>(control, "Offline profile tables");
                    items.SelectedIndex = 0;
                    var filters = Find<ComboBox>(control, "Generated filter records");
                    filters.Items.Add(new OfflineGeneratedFilter
                    {
                        Id = Guid.NewGuid(), FetchXml = "[\"\"]",
                        OfflineSql = "/*SelectStart*/ select [accountid] from account"
                    });
                    filters.SelectedIndex = 0;
                    Assert.Equal("[\"\"]", Find<RichTextBox>(control, "Generated offline FetchXML").Text);
                    Assert.Contains("placeholder", Find<System.Windows.Forms.Label>(control, "Generated definition coverage").Text);
                    var sql = Find<TextBox>(control, "Generated offline SQL");
                    Assert.True(sql.ReadOnly);
                    Assert.StartsWith("/*SelectStart*/", sql.Text);
                    Assert.True(Find<RichTextBox>(control, "Configured offline FetchXML").ReadOnly);
                    items.SelectedIndex = 1;
                    Assert.Empty(sql.Text);
                    Assert.Empty(filters.Items);
                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), "", null!);
                    Assert.Empty(profiles.Items);
                    Assert.Empty(items.Items);
                    Assert.Empty(filters.Items);
                    Assert.False(Find<Button>(control, "Load tables").Enabled);
                    Assert.False(Find<Button>(control, "Load generated definitions").Enabled);
                }
            });
        }

        [Fact]
        public void OpeningAPersonalViewRestoresOnlineAnalysisAfterOpeningAnOfflineFilter()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    const string xml = "<fetch><entity name='account'><all-attributes/></entity></fetch>";
                    var items = Find<CheckedListBox>(control, "Offline profile tables");
                    items.Items.Add(new OfflineProfileItem { ConfiguredXml = xml }, true);
                    Invoke(control, "OpenOfflineXml");
                    var views = Find<CheckedListBox>(control, "Personal views to analyse");
                    views.Items.Add(new ViewSource { Name = "Synthetic", Xml = xml }, true);
                    Invoke(control, "OpenViewXml");
                    var rules = Find<CheckedListBox>(control, "Enabled analysis rules");
                    for (var index = 0; index < rules.Items.Count; index++)
                        rules.SetItemChecked(index, ((RuleDefinition)rules.Items[index]).Id == "FXL001");
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    var entry = Assert.IsType<AnalysisEntry>(Assert.Single(Find<ComboBox>(control, "Analysis results").Items.Cast<object>()));
                    Assert.Equal(AnalysisSurface.OnlineQuery, entry.Result.Surface);
                    Assert.Null(entry.OfflineSource);
                    Assert.Equal("Personal view", Find<System.Windows.Forms.Label>(control, "Analysis context").Text);
                    Assert.Contains("Local personal view", entry.SourceName);
                    Assert.Contains(entry.Result.Findings, f => f.RuleId == "FXL001");
                }
            });
        }

        [Fact]
        public void RuleSubsetAppliesToManualAndCollectedViewsWithoutRequiringMetadataConnection()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var rules = Find<CheckedListBox>(control, "Enabled analysis rules");
                    for (var index = 0; index < rules.Items.Count; index++)
                        rules.SetItemChecked(index, ((RuleDefinition)rules.Items[index]).Id == "FXL002");
                    const string xml = "<fetch no-lock='true'><entity name='account'><attribute name='name'/>" +
                        "<filter><condition attribute='name' operator='like' value='%sample'/></filter></entity></fetch>";
                    Find<RichTextBox>(control, "FetchXML").Text = xml;
                    var requests = 0;
                    control.OnRequestConnection += (_, __) => requests++;
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    var results = Find<ComboBox>(control, "Analysis results");
                    var manual = Assert.IsType<AnalysisEntry>(Assert.Single(results.Items.Cast<object>()));
                    Assert.Equal(new[] { "FXL002" }, manual.Result.SelectedRuleIds);
                    Assert.Equal(AnalysisStatus.Complete, manual.Result.Status);
                    Assert.Single(manual.Result.Findings);
                    var views = Find<CheckedListBox>(control, "Personal views to analyse");
                    views.Items.Add(new ViewSource { Name = "Synthetic view", Xml = xml }, true);
                    Invoke(control, "AnalyseViews");
                    WaitForIdle(control);
                    var view = Assert.IsType<AnalysisEntry>(Assert.Single(results.Items.Cast<object>()));
                    Assert.Equal("Synthetic view", view.SourceName);
                    Assert.Equal(new[] { "FXL002" }, view.Result.SelectedRuleIds);
                    Assert.Single(view.Result.Findings);
                    Assert.Equal(0, requests);

                    for (var index = 0; index < rules.Items.Count; index++) rules.SetItemChecked(index, true);
                    Assert.Equal(new[] { "FXL002" }, view.Result.SelectedRuleIds);
                }
            });
        }

        [Fact]
        public void EmptyRuleSelectionAndOptionalLocalFilterDoNotRequestConnection()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var requests = 0;
                    control.OnRequestConnection += (_, __) => requests++;
                    var rules = Find<CheckedListBox>(control, "Enabled analysis rules");
                    for (var index = 0; index < rules.Items.Count; index++) rules.SetItemChecked(index, false);
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    Assert.Empty(Find<ComboBox>(control, "Analysis results").Items);
                    Assert.Contains("at least one analysis rule", Find<System.Windows.Forms.Label>(control, "Workflow status").Text);
                    var filter = Find<TextBox>(control, "Filter users");
                    filter.Text = "a";
                    filter.Clear();
                    Assert.Equal(0, requests);
                }
            });
        }

        private static IEnumerable<Control> Descendants(Control root) =>
            root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));

        private static T Find<T>(Control root, string name) where T : Control =>
            Descendants(root).OfType<T>().Single(c => c.AccessibleName == name);

        private static void Invoke(LinterControl control, string method, params object[] args) =>
            typeof(LinterControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, args);

        private static void WaitForIdle(LinterControl control)
        {
            var elapsed = Stopwatch.StartNew();
            while (typeof(LinterControl).GetField("cancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control) != null)
            {
                Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), "The background operation did not complete.");
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        [Fact]
        public void UserSelectionsSurviveFilteringAndRefreshAndActionsFollowTheirData()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var a = new UserTarget { Id = Guid.NewGuid(), Name = "Synthetic Alpha", Email = "one@example.invalid" };
                    var b = new UserTarget { Id = Guid.NewGuid(), Name = "Synthetic Beta", Email = "two@example.invalid" };
                    var directory = new UserLoadResult { Complete = true, Outcome = "Directory complete." };
                    directory.Users.AddRange(new[] { a, b });
                    Invoke(control, "PresentUsers", directory);
                    var filter = Find<TextBox>(control, "Filter users");
                    var users = Find<CheckedListBox>(control, "Users");
                    var load = Find<Button>(control, "Load Views");
                    var analyse = Find<Button>(control, "Analyse");
                    var open = Find<Button>(control, "Open XML");
                    Assert.False(load.Enabled);
                    Assert.False(analyse.Enabled);
                    Assert.False(open.Enabled);
                    users.SetItemChecked(0, true);
                    filter.Text = "TWO@";
                    Assert.Same(b, Assert.Single(users.Items.Cast<object>()));
                    users.SetItemChecked(0, true);
                    Assert.Contains("2 selected", Find<System.Windows.Forms.Label>(control, "User counts").Text);
                    filter.Text = "no-match";
                    Assert.Empty(users.Items);
                    Assert.True(load.Enabled);
                    filter.Text = a.Id.ToString();
                    Assert.Same(a, Assert.Single(users.CheckedItems.Cast<object>()));
                    filter.Clear();
                    Assert.Equal(2, users.CheckedItems.Count);
                    Invoke(control, "PresentUsers", directory);
                    Assert.Equal(2, users.CheckedItems.Count);
                    var views = Find<CheckedListBox>(control, "Personal views to analyse");
                    views.Items.Add(new ViewSource { Name = "Synthetic view" }, true);
                    views.SelectedIndex = 0;
                    Assert.True(analyse.Enabled);
                    Assert.True(open.Enabled);
                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), "", null!);
                    Assert.Empty(users.Items);
                    Assert.False(load.Enabled);
                    Assert.False(analyse.Enabled);
                    Assert.False(open.Enabled);
                    Assert.Contains("0 loaded", Find<System.Windows.Forms.Label>(control, "User counts").Text);
                }
            });
        }

        [Fact]
        public void InvalidXmlReplacesFindingsAndConnectionChangeClearsReadableResults()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var editor = Find<RichTextBox>(control, "FetchXML");
                    editor.Text = "<fetch><entity name='account'><attribute name='name'/><filter>" +
                        "<condition attribute='name' operator='like' value='%sample'/></filter></entity></fetch>";
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    var grid = Find<DataGridView>(control, "Findings");
                    Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                    editor.Text = "<fetch>";
                    Invoke(control, "AnalyseManual");
                    WaitForIdle(control);
                    Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
                    Assert.Contains("Cannot analyse", Find<System.Windows.Forms.Label>(control, "Analysis summary").Text);
                    var explanation = Find<TextBox>(control, "Finding explanation and next steps");
                    Assert.Contains("Invalid or unsafe XML", explanation.Text);
                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), "", null!);
                    Assert.Empty(Find<ComboBox>(control, "Analysis results").Items);
                    Assert.Empty(explanation.Text);
                    Assert.Empty(Find<TextBox>(control, "Technical evidence and coverage").Text);
                    Assert.Contains("No analysis yet", Find<System.Windows.Forms.Label>(control, "Analysis summary").Text);
                }
            });
        }

        [Fact]
        public void OpenXmlUsesOneCheckedViewNotTheHighlightedViewAndDoesNotAnalyse()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var views = Find<CheckedListBox>(control, "Personal views to analyse");
                    var open = Find<Button>(control, "Open XML");
                    var navigation = Find<ListBox>(control, "Task navigation");
                    var first = new ViewSource { Name = "Checked view", Xml = "<fetch><entity name='account'/></fetch>", Layout = "<grid/>" };
                    views.Items.Add(first, true);
                    views.Items.Add(new ViewSource { Name = "Highlighted view", Xml = "<fetch><entity name='contact'/></fetch>" });
                    views.SelectedIndex = 1;
                    Assert.True(open.Enabled);
                    Invoke(control, "OpenViewXml");
                    Assert.True(XNode.DeepEquals(XDocument.Parse(first.Xml), XDocument.Parse(Find<RichTextBox>(control, "FetchXML").Text)));
                    Assert.True(XNode.DeepEquals(XDocument.Parse(first.Layout), XDocument.Parse(Find<RichTextBox>(control, "Optional layout XML").Text)));
                    Assert.True(Find<RichTextBox>(control, "FetchXML").Lines.Length > 1);
                    Assert.Equal("FetchXML query", navigation.SelectedItem);
                    Assert.Contains(first.Name, Find<System.Windows.Forms.Label>(control, "Query source").Text);
                    Assert.Empty(Find<ComboBox>(control, "Analysis results").Items);
                    views.SetItemChecked(1, true);
                    Assert.False(open.Enabled);
                    Invoke(control, "OpenViewXml");
                    Assert.Contains("exactly one", Find<System.Windows.Forms.Label>(control, "Workflow status").Text);
                    views.SetItemChecked(0, false);
                    views.SetItemChecked(1, false);
                    Assert.False(open.Enabled);
                }
            });
        }

        [Fact]
        public void XmlFormattingPreservesValuesMixedContentAndWhitespaceScopesAndIsRepeatable()
        {
            const string source = "<?xml version='1.0' encoding='utf-8'?>\n<fetch><!--note--><entity name='account'>" +
                "<filter><condition value='line&#xA;tab&#x9;carriage&#xD;&amp;&quot;'>" +
                "<value>   </value><value> before <inner/> after </value>" +
                "<value xml:space='preserve'><inner/><inner/></value>" +
                "<value><![CDATA[ <raw>&text ]]></value></condition></filter></entity></fetch>";
            var formatted = XmlEditor.Format(source);
            Assert.Contains("\n  <entity", formatted);
            Assert.Contains("<value>   </value>", formatted);
            Assert.Contains("<value xml:space=\"preserve\"><inner /><inner /></value>", formatted);
            Assert.True(XNode.DeepEquals(XDocument.Parse(source), XDocument.Parse(formatted)));
            Assert.Equal(formatted, XmlEditor.Format(formatted));
            Assert.Equal("utf-8", XDocument.Parse(formatted).Declaration!.Encoding);
        }

        [Theory]
        [InlineData("<fetch>")]
        [InlineData("<!DOCTYPE fetch [<!ENTITY external SYSTEM 'file:///unread'>]><fetch>&external;</fetch>")]
        public void XmlFormattingRejectsInvalidOrUnsafeXml(string source) =>
            Assert.Throws<XmlException>(() => XmlEditor.Format(source));

        [Fact]
        public void XmlFormattingBoundsInputDepthAndIndentedOutput()
        {
            Assert.Throws<XmlException>(() => XmlEditor.Format(new string(' ', Linter.MaximumXmlCharacters + 1)));
            var deep = string.Concat(Enumerable.Repeat("<x>", 70)) + string.Concat(Enumerable.Repeat("</x>", 70));
            Assert.Throws<XmlException>(() => XmlEditor.Format(deep));
            var wide = "<fetch>" + string.Concat(Enumerable.Repeat("<x/>", 140000)) + "</fetch>";
            Assert.Throws<XmlException>(() => XmlEditor.Format(wide));
        }

        [Fact]
        public void XmlEditorsColourLoadedDefinitionsAndFormatPastedXmlAsAnUndoableLocalEdit()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var query = Find<XmlEditor>(control, "FetchXML");
                    var layout = Find<XmlEditor>(control, "Optional layout XML");
                    const string source = "<fetch><!--note--><entity name='account'><filter>" +
                        "<condition value='C:\\{sample}\u00E9\U0001F527 &amp; &quot;'/></filter></entity></fetch>";
                    Assert.Empty(query.LoadXml(source));
                    Assert.Contains("\u00E9\U0001F527", query.Text);
                    Assert.Contains("\\{sample}", query.Text);
                    Assert.True(XNode.DeepEquals(XDocument.Parse(source), XDocument.Parse(query.Text)));
                    query.Select(query.Text.IndexOf("entity", StringComparison.Ordinal), 6);
                    var elementColour = query.SelectionColor;
                    query.Select(query.Text.IndexOf("name", StringComparison.Ordinal), 4);
                    var attributeColour = query.SelectionColor;
                    query.Select(query.Text.IndexOf("account", StringComparison.Ordinal), 7);
                    Assert.NotEqual(elementColour, attributeColour);
                    Assert.NotEqual(attributeColour, query.SelectionColor);
                    Assert.Empty(layout.LoadXml("<grid><row><cell name='name'/></row></grid>"));
                    Assert.True(layout.Lines.Length > 1);
                    query.Text = source;
                    query.ClearUndo();
                    Invoke(control, "FormatXml");
                    Assert.True(query.Lines.Length > 1);
                    Assert.True(query.CanUndo);
                    query.Undo();
                    Assert.Equal(source, query.Text);
                    Assert.Empty(Find<ComboBox>(control, "Analysis results").Items);
                    Assert.Contains("Saved definitions are unchanged", Find<System.Windows.Forms.Label>(control, "Workflow status").Text);
                    Assert.NotEmpty(query.LoadXml("<fetch>"));
                    Assert.Equal("<fetch>", query.Text);
                }
            });
        }

        [Fact]
        public void FormattingInvalidIncludedLayoutDoesNotPartiallyChangeTheQuery()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var query = Find<XmlEditor>(control, "FetchXML");
                    const string source = "<fetch><entity name='account'/></fetch>";
                    query.Text = source;
                    Find<XmlEditor>(control, "Optional layout XML").Text = "<grid>";
                    Descendants(control).OfType<CheckBox>().Single(c => c.Text == "Include layout XML").Checked = true;
                    var error = Assert.Throws<TargetInvocationException>(() => Invoke(control, "FormatXml"));
                    Assert.IsType<XmlException>(error.InnerException);
                    Assert.Equal(source, query.Text);
                }
            });
        }

        [Fact]
        public void CsvLoadingRequiresCheckedCandidatesAndUserLoadingHasOneContextualAction()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    var load = Find<Button>(control, "Load matching views");
                    var candidates = Find<CheckedListBox>(control, "Validated CSV candidates");
                    Assert.False(load.Enabled);
                    candidates.Items.Add(new Candidate());
                    Assert.False(load.Enabled);
                    candidates.SetItemChecked(0, true);
                    Assert.True(load.Enabled);
                    candidates.SetItemChecked(0, false);
                    Assert.False(load.Enabled);
                    Assert.Single(Descendants(control).OfType<Button>(), b => b.Text == "Load users");
                    Invoke(control, "PresentUsers", new UserLoadResult { Complete = true });
                    Assert.Single(Descendants(control).OfType<Button>(), b => b.Text == "Refresh users");
                    candidates.SetItemChecked(0, true);
                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), "", null!);
                    Assert.False(load.Enabled);
                    Assert.Empty(candidates.Items);
                    Assert.NotNull(Find<Button>(control, "Load users"));
                }
            });
        }

        [Fact]
        public void SlowWorkShowsModalProgressAndCancellationKeepsTheUiResponsive()
        {
            RunSta(() =>
            {
                using (var form = new Form())
                using (var control = new LinterControl())
                using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    form.Controls.Add(control);
                    form.Show();
                    var sawProgress = false;
                    var applied = false;
                    timer.Tick += (_, __) =>
                    {
                        var dialog = Application.OpenForms.OfType<OperationDialog>().SingleOrDefault();
                        if (dialog == null || !dialog.Visible) return;
                        Assert.True(dialog.Modal);
                        sawProgress = true;
                        Descendants(dialog).OfType<Button>().Single(b => b.Text == "Cancel").PerformClick();
                        timer.Stop();
                    };
                    timer.Start();
                    control.Run((token, progress) =>
                    {
                        progress.Report("Waiting for synthetic work.");
                        if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("Cancellation was not received.");
                        token.ThrowIfCancellationRequested();
                        return 1;
                    }, _ => applied = true, "Testing cancellation");
                    WaitForIdle(control);
                    Assert.True(sawProgress);
                    Assert.False(applied);
                    Assert.DoesNotContain(Application.OpenForms.Cast<Form>(), f => f is OperationDialog);
                    Assert.True(Find<ListBox>(control, "Task navigation").Enabled);
                    Assert.Contains("Cancelled", Find<System.Windows.Forms.Label>(control, "Workflow status").Text);
                }
            });
        }

        [Fact]
        public void QuickWorkDoesNotShowAModalAndLateProgressDoesNotOverwriteCompletion()
        {
            RunSta(() =>
            {
                using (var form = new Form())
                using (var control = new LinterControl())
                using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
                {
                    form.Controls.Add(control);
                    form.Show();
                    var sawProgress = false;
                    IProgress<string>? progress = null;
                    timer.Tick += (_, __) => sawProgress |= Application.OpenForms.OfType<OperationDialog>().Any();
                    timer.Start();
                    var status = Find<System.Windows.Forms.Label>(control, "Workflow status");
                    control.Run((token, reporter) =>
                    {
                        progress = reporter;
                        return 42;
                    }, value => status.Text = "Finished: " + value, "Quick analysis");
                    WaitForIdle(control);
                    progress!.Report("Late progress");
                    Application.DoEvents();
                    Assert.False(sawProgress);
                    Assert.Equal("Finished: 42", status.Text);
                    Assert.All(Descendants(control).OfType<Button>(), b => Assert.False(string.IsNullOrWhiteSpace(b.AccessibleDescription)));
                }
            });
        }

        [Fact]
        public void DisposingControlCancelsWorkWithoutPublishingResultsOrOpeningProgress()
        {
            RunSta(() =>
            {
                using (var form = new Form())
                using (var control = new LinterControl())
                {
                    form.Controls.Add(control);
                    form.Show();
                    var applied = false;
                    control.Run((token, progress) =>
                    {
                        if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                        token.ThrowIfCancellationRequested();
                        return 1;
                    }, _ => applied = true, "Testing disposal");
                    control.Dispose();
                    WaitForIdle(control);
                    Assert.False(applied);
                    Assert.DoesNotContain(Application.OpenForms.Cast<Form>(), f => f is OperationDialog);
                }
            });
        }

        [Fact]
        public void ConnectionChangeDiscardsBackgroundResultsAndRestoresActionGates()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                using (var release = new ManualResetEventSlim())
                {
                    var applied = false;
                    control.Run((token, progress) =>
                    {
                        if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                        return 1;
                    }, _ => applied = true, "Testing stale result");
                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), "", null!);
                    release.Set();
                    WaitForIdle(control);
                    Assert.False(applied);
                    Assert.False(Find<Button>(control, "Load matching views").Enabled);
                    Assert.False(Find<Button>(control, "Open XML").Enabled);
                }
            });
        }

        [Fact]
        public void DisconnectedClosureWaitsForConnectionThenReplaysOnce()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    RequestConnectionEventArgs? request = null;
                    control.OnRequestConnection += (_, args) => request = Assert.IsType<RequestConnectionEventArgs>(args);
                    var calls = 0;
                    control.ExecuteWithConnection(() => calls++);
                    Assert.Equal(0, calls);
                    Assert.NotNull(request);
                    Assert.Same(control, request!.Control);
                    Assert.Equal("Recaller", request.ActionName);
                    Assert.IsType<ExternalMethodCallerInfo>(request.Parameter);

                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), request.ActionName, request.Parameter);
                    Assert.Equal(1, calls);
                }
            });
        }

        [Fact]
        public void ConnectedClosureRunsWithoutRequestingAnotherConnection()
        {
            RunSta(() =>
            {
                using (var control = new LinterControl())
                {
                    control.UpdateConnection(new NoRequestsService(), new ConnectionDetail(), string.Empty, null!);
                    var requests = 0;
                    control.OnRequestConnection += (_, __) => requests++;
                    var calls = 0;
                    control.ExecuteWithConnection(() => calls++);
                    Assert.Equal(1, calls);
                    Assert.Equal(0, requests);
                }
            });
        }

        private static void RunSta(Action action)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        private sealed class NoRequestsService : IOrganizationService
        {
            public Guid Create(Entity entity) => throw new NotSupportedException();
            public void Update(Entity entity) => throw new NotSupportedException();
            public void Delete(string entityName, Guid id) => throw new NotSupportedException();
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
            public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
            public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }
    }
}
