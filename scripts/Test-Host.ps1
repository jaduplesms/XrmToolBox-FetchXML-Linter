param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$InstalledPluginPath,
    [string]$ShortlistCsvPath,
    [string]$ScreenshotDirectory
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this .NET Framework host check with Windows PowerShell 5.1 and -STA.'
}
$root = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $root "src\FetchXmlLinter.XrmToolBox\bin\$Configuration\net48"
$pluginPath = Join-Path $runtime 'FetchXmlLinter.XrmToolBox.dll'
if ($InstalledPluginPath) {
    $pluginPath = (Resolve-Path -LiteralPath $InstalledPluginPath).Path
}
$resolver = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = [System.Reflection.AssemblyName]::new($eventArgs.Name).Name
    if ($name -eq 'FetchXmlLinter.Core') { return $null }
    foreach ($extension in @('dll', 'exe')) {
        $path = Join-Path $runtime "$name.$extension"
        if (Test-Path -LiteralPath $path) {
            return [System.Reflection.Assembly]::LoadFrom($path)
        }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$form = $null
$control = $null
try {
    Add-Type -AssemblyName System.Windows.Forms
    $assembly = if ($InstalledPluginPath) {
        [System.Reflection.Assembly]::LoadFrom($pluginPath)
    } else {
        [System.Reflection.Assembly]::Load([IO.File]::ReadAllBytes($pluginPath))
    }
    $plugin = $assembly.CreateInstance('FetchXmlLinter.XrmToolBox.Plugin')
    $control = $plugin.GetControl()
    $form = [System.Windows.Forms.Form]::new()
    $form.Text = 'FetchXML Linter - disconnected smoke check'
    $form.Size = [System.Drawing.Size]::new(1100, 850)
    $form.Controls.Add($control)
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()

    function Get-Descendants($parent) {
        foreach ($child in $parent.Controls) {
            $child
            Get-Descendants $child
        }
    }
    function Save-Preview([string]$name) {
        if (-not $ScreenshotDirectory) { return }
        if (-not ('FetchXmlLinter.XmlPreview' -as [type])) {
            Add-Type -ReferencedAssemblies System.Drawing,System.Windows.Forms -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace FetchXmlLinter {
    public static class XmlPreview {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Range { public int Start, End; }
        [StructLayout(LayoutKind.Sequential)]
        private struct FormatRange {
            public IntPtr Hdc, Target;
            public Rect Area, Page;
            public Range Characters;
        }
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, ref FormatRange range);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr range);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, out Rect area);

        public static void Draw(RichTextBox editor, Control root, Bitmap destination) {
            using (var bitmap = new Bitmap(editor.ClientSize.Width, editor.ClientSize.Height))
            using (var graphics = Graphics.FromImage(bitmap)) {
                graphics.Clear(editor.BackColor);
                Rect pixels;
                SendMessage(editor.Handle, 0x00B2, IntPtr.Zero, out pixels); // EM_GETRECT
                var area = new Rect {
                    Left = (int)(pixels.Left * 1440 / graphics.DpiX),
                    Top = (int)(pixels.Top * 1440 / graphics.DpiY),
                    Right = (int)(pixels.Right * 1440 / graphics.DpiX),
                    Bottom = (int)(pixels.Bottom * 1440 / graphics.DpiY)
                };
                var hdc = graphics.GetHdc();
                try {
                    var range = new FormatRange {
                        Hdc = hdc, Target = hdc, Area = area, Page = area,
                        Characters = new Range { Start = editor.GetCharIndexFromPosition(new Point(1, 1)), End = editor.TextLength }
                    };
                    SendMessage(editor.Handle, 0x0439, new IntPtr(1), ref range); // EM_FORMATRANGE
                }
                finally {
                    SendMessage(editor.Handle, 0x0439, IntPtr.Zero, IntPtr.Zero);
                    graphics.ReleaseHdc(hdc);
                }
                using (var target = Graphics.FromImage(destination))
                    target.DrawImageUnscaled(bitmap, root.PointToClient(editor.PointToScreen(Point.Empty)));
            }
        }
    }
}
'@
        }
        [IO.Directory]::CreateDirectory($ScreenshotDirectory) | Out-Null
        $bitmap = [Drawing.Bitmap]::new($control.ClientSize.Width, $control.ClientSize.Height)
        try {
            $control.DrawToBitmap($bitmap, [Drawing.Rectangle]::new([Drawing.Point]::Empty, $control.ClientSize))
            # DrawToBitmap omits native RichTextBox text; render only those client areas separately.
            foreach ($child in (Get-Descendants $control)) {
                if ($child -is [Windows.Forms.RichTextBox] -and $child.Visible) {
                    [FetchXmlLinter.XmlPreview]::Draw($child, $control, $bitmap)
                }
            }
            $bitmap.Save((Join-Path $ScreenshotDirectory "$name.png"), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bitmap.Dispose() }
    }
    function Wait-Operation {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $field = $control.GetType().GetField('cancellation', [Reflection.BindingFlags]'Instance,NonPublic')
        while ($field.GetValue($control)) {
            if ($watch.Elapsed.TotalSeconds -gt 15) { throw 'Background analysis did not finish.' }
            [System.Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 5
        }
    }
    $all = @(Get-Descendants $control)
    $navigation = @($all | Where-Object AccessibleName -eq 'Task navigation')[0]
    if ($navigation.Items.Count -ne 5 -or $navigation.SelectedIndex -ne 0) { throw 'Workflow navigation did not start at Prepare usage KQL.' }
    Save-Preview 'prepare-kql'
    $navigation.SelectedIndex = 3
    [System.Windows.Forms.Application]::DoEvents()
    $logo = @($all | Where-Object AccessibleName -eq 'FetchXML Linter logo')[0]
    if (-not $logo.Visible -or -not $logo.Image -or $logo.Image.Width -ne 80 -or $logo.Image.Height -ne 80) {
        throw 'The approved logo was not loaded into the tool header.'
    }
    $editor = @($all | Where-Object AccessibleName -eq 'FetchXML')[0]
    $button = @($all | Where-Object AccessibleName -eq 'Analyse query')[0]
    $list = @($all | Where-Object AccessibleName -eq 'Analysis results')[0]
    $editor.Text = "<fetch><entity name='account'><attribute name='name'/><filter><condition attribute='name' operator='like' value='%synthetic'/></filter></entity></fetch>"
    $format = @($all | Where-Object AccessibleName -eq 'Format XML')[0]
    $format.PerformClick()
    if ($editor -isnot [Windows.Forms.RichTextBox] -or $editor.Lines.Count -lt 3 -or $editor.Rtf -notmatch '\\colortbl') {
        throw 'FetchXML was not indented and syntax-coloured.'
    }
    $button.PerformClick()
    Wait-Operation
    [System.Windows.Forms.Application]::DoEvents()
    if ($list.Items.Count -ne 1 -or $list.Items[0].Result.Findings.RuleId -notcontains 'FXL002') {
        throw 'Disconnected manual linting did not produce the expected evidence.'
    }
    $grid = @($all | Where-Object AccessibleName -eq 'Findings')[0]
    $explanation = @($all | Where-Object AccessibleName -eq 'Finding explanation and next steps')[0]
    if ($grid.Rows.Count -ne 1 -or $explanation.Text -notmatch 'Leading-wildcard filtering') {
        throw 'The findings table did not display the selectable explanation.'
    }
    Save-Preview 'query-results'
    $includeLayout = @($all | Where-Object { $_ -is [Windows.Forms.CheckBox] -and $_.Text -eq 'Include layout XML' })[0]
    $layoutEditor = @($all | Where-Object AccessibleName -eq 'Optional layout XML')[0]
    $includeLayout.Checked = $true
    $layoutEditor.Text = "<grid name='resultset'><row name='result'><cell name='name' width='200'/></row></grid>"
    $format.PerformClick()
    if ($layoutEditor -isnot [Windows.Forms.RichTextBox] -or $layoutEditor.Lines.Count -lt 3) {
        throw 'Layout XML was not indented.'
    }
    Save-Preview 'query-and-layout'
    $includeLayout.Checked = $false
    $core = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'FetchXmlLinter.Core' })[0]
    $csv = "SchemaVersion,UserId,UserIdType,ViewId,Attribution,EntityName,WindowStartUtc,WindowEndUtc,SampleCount,P95Ms,MetricKind`n" +
        '1,22222222-2222-2222-2222-222222222222,DataverseSystemUserId,33333333-3333-3333-3333-333333333333,ExactViewMarker,account,,,,,'
    if ($ShortlistCsvPath) {
        $csv = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $ShortlistCsvPath).Path)
    }
    $organization = [Guid]'11111111-1111-1111-1111-111111111111'
    $shortlist = $core.GetType('FetchXmlLinter.Core.ShortlistCsv').GetMethod('Read').Invoke(
        $null, [object[]]@($csv, $organization))
    if ($shortlist.Errors.Count -gt 0 -or $shortlist.Candidates.Count -eq 0 -or
        (-not $ShortlistCsvPath -and $shortlist.Candidates.Count -ne 1) -or
        @($shortlist.Candidates | Where-Object OrganizationId -ne $organization).Count -gt 0) {
        throw "The plugin's loaded importer rejected organization-free CSV: $($shortlist.Errors -join '; ')"
    }
    if (-not $core -or $core.Location) {
        throw 'The core library was not loaded from the plugin embedded resource.'
    }
    Write-Output "Loaded embedded core $($core.GetName().Version): $($shortlist.Candidates.Count) CSV candidates accepted, no errors."
    $navigation.SelectedIndex = 4
    [System.Windows.Forms.Application]::DoEvents()
    $clear = @($all | Where-Object AccessibleName -eq 'Clear all')[0]
    $clear.PerformClick()
    $navigation.SelectedIndex = 3
    $button.PerformClick()
    if ($navigation.SelectedIndex -ne 4 -or $list.Items.Count -ne 1) {
        throw 'An empty rule selection was not blocked without replacing existing results.'
    }
    $rules = @($all | Where-Object AccessibleName -eq 'Enabled analysis rules')[0]
    for ($index = 0; $index -lt $rules.Items.Count; $index++) {
        $rules.SetItemChecked($index, $rules.Items[$index].Id -eq 'FXL002')
    }
    Save-Preview 'analysis-rules'
    $navigation.SelectedIndex = 3
    $button.PerformClick()
    Wait-Operation
    if ($list.Items[0].Result.Status.ToString() -ne 'Complete' -or
        $list.Items[0].Result.SelectedRuleIds.Count -ne 1 -or
        $list.Items[0].Result.Findings.RuleId -notcontains 'FXL002') {
        throw 'The selected XML-only rule did not run independently of disabled metadata checks.'
    }
    $requests = [Collections.Generic.List[object]]::new()
    $control.add_OnRequestConnection([EventHandler] {
        param($sender, $eventArgs)
        $requests.Add($eventArgs)
    })
    $navigation.SelectedIndex = 2
    [System.Windows.Forms.Application]::DoEvents()
    $search = @($all | Where-Object AccessibleName -eq 'Filter users')[0]
    $search.Text = 'a'
    if ($requests.Count -ne 0) { throw 'Local user filtering unexpectedly requested a connection.' }
    $search.Clear()
    $find = $control.GetType().GetMethod('LoadUsers', [Reflection.BindingFlags]'Instance,NonPublic')
    $find.Invoke($control, $null)
    if ($requests.Count -ne 1 -or $requests[0].ActionName -ne 'Recaller') {
        throw 'User loading did not request a connection with a replayable callback.'
    }
    $directory = [Activator]::CreateInstance($assembly.GetType('FetchXmlLinter.XrmToolBox.UserLoadResult'), $true)
    $directory.Complete = $true
    $directory.Outcome = 'Synthetic directory complete.'
    for ($index = 0; $index -lt 10001; $index++) {
        $user = [Activator]::CreateInstance($assembly.GetType('FetchXmlLinter.XrmToolBox.UserTarget'), $true)
        $user.Id = [Guid]::NewGuid()
        $user.Name = 'Synthetic user {0:D5}' -f $index
        $user.Email = "synthetic$index@example.invalid"
        $directory.Users.Add($user)
    }
    $present = $control.GetType().GetMethod('PresentUsers', [Reflection.BindingFlags]'Instance,NonPublic')
    $present.Invoke($control, [object[]]@($directory))
    $usersList = @($all | Where-Object AccessibleName -eq 'Users')[0]
    $userCounts = @($all | Where-Object AccessibleName -eq 'User counts')[0]
    $loadViews = @($all | Where-Object AccessibleName -eq 'Load Views')[0]
    if ($usersList.Items.Count -ne 10001) { throw 'The user picker truncated the synthetic directory.' }
    $usersList.SetItemChecked(0, $true)
    $search.Text = 'synthetic10000@'
    if ($usersList.Items.Count -ne 1 -or -not $loadViews.Enabled -or $userCounts.Text -ne '1 selected | 10001 loaded | 1 shown') {
        throw 'Local filtering lost a hidden selection or required actions.'
    }
    $search.Clear()
    if (-not $usersList.GetItemChecked(0) -or $requests.Count -ne 1) {
        throw 'Filtering did not preserve the checked user without additional connection work.'
    }
    [System.Windows.Forms.Application]::DoEvents()
    Save-Preview 'personal-views'
    $form.Size = [System.Drawing.Size]::new(850, 650)
    [System.Windows.Forms.Application]::DoEvents()
    Save-Preview 'compact-personal-views'
    if ($usersList.Height -lt 60) { throw "The compact user picker is not usable: $($usersList.Size)." }
    $selectors = @($all | Where-Object AccessibleName -eq 'Personal view selectors')[0]
    $buttonBounds = $selectors.RectangleToClient($loadViews.RectangleToScreen($loadViews.ClientRectangle))
    if (-not $selectors.ClientRectangle.Contains($buttonBounds)) {
        throw 'Load Views is not visible without scrolling the compact selectors.'
    }
    $workspace = @($all | Where-Object AccessibleName -eq 'Input and results splitter')[0]
    $ratio = $workspace.SplitterDistance / ($workspace.Height - $workspace.SplitterWidth)
    if ([Math]::Abs($ratio - 0.6) -gt 0.03) { throw "Default input/results ratio is not 60/40: $ratio" }
    foreach ($name in @('Open XML', 'Analyse', 'Refresh users')) {
        $action = @($all | Where-Object { $_ -is [Windows.Forms.Button] -and $_.AccessibleName -eq $name })[0]
        $bounds = $selectors.RectangleToClient($action.RectangleToScreen($action.ClientRectangle))
        if (-not $action.Visible -or -not $selectors.ClientRectangle.Contains($bounds)) {
            throw "A compact selector action is clipped: $name"
        }
    }
    $viewList = @($all | Where-Object AccessibleName -eq 'Personal views to analyse')[0]
    if ($viewList.Height -lt 50) { throw "The compact personal-view list is too short: $($viewList.Height)." }
    Save-Preview 'compact-personal-actions'
    $workspace.SplitterDistance = [int](($workspace.Height - $workspace.SplitterWidth) * 0.55)
    $adjustedRatio = $workspace.SplitterDistance / ($workspace.Height - $workspace.SplitterWidth)
    $form.Size = [System.Drawing.Size]::new(1100, 850)
    [System.Windows.Forms.Application]::DoEvents()
    $resizedRatio = $workspace.SplitterDistance / ($workspace.Height - $workspace.SplitterWidth)
    if ([Math]::Abs($resizedRatio - $adjustedRatio) -gt 0.01) { throw 'Resizing lost the manually adjusted splitter ratio.' }
    $navigation.SelectedIndex = 0
    $navigation.SelectedIndex = 2
    [System.Windows.Forms.Application]::DoEvents()
    $returnedRatio = $workspace.SplitterDistance / ($workspace.Height - $workspace.SplitterWidth)
    if ([Math]::Abs($returnedRatio - $adjustedRatio) -gt 0.01) { throw 'Navigation lost the manually adjusted splitter ratio.' }
    $workspace.SplitterDistance = [int](($workspace.Height - $workspace.SplitterWidth) * 0.6)
    $navigation.SelectedIndex = 1
    [System.Windows.Forms.Application]::DoEvents()
    $matching = @($all | Where-Object AccessibleName -eq 'Load matching views')[0]
    if ($matching.Enabled) { throw 'Load matching views must be disabled before candidates are checked.' }
    Save-Preview 'usage-csv'
    $navigation.SelectedIndex = 0
    [System.Windows.Forms.Application]::DoEvents()
    $kql = @($all | Where-Object { $_ -is [System.Windows.Forms.TextBox] -and $_.AccessibleName -eq 'KQL template' })[0]
    $templates = @($all | Where-Object { $_ -is [System.Windows.Forms.ComboBox] -and $_.AccessibleName -eq 'KQL template' })[0]
    for ($index = 0; $index -lt $templates.Items.Count; $index++) {
        $templates.SelectedIndex = $index
        [System.Windows.Forms.Application]::DoEvents()
        $name = [string]$templates.SelectedItem
        $expected = [IO.File]::ReadAllText((Join-Path $root "kql\$name"))
        if ($kql.Text -cne $expected) {
            throw "The displayed embedded KQL template does not match its source: $name"
        }
    }
    $form.Size = [System.Drawing.Size]::new(850, 650)
    $navigation.SelectedIndex = 3
    [System.Windows.Forms.Application]::DoEvents()
    Save-Preview 'compact-results'
    if (-not $grid.Visible -or $grid.Height -lt 85 -or $editor.Height -lt 50) {
        throw "The compact layout did not retain usable query and results areas: grid=$($grid.Size), editor=$($editor.Size)."
    }
    $changeRules = @($all | Where-Object AccessibleName -eq 'Change rules')[0]
    foreach ($element in @($grid, $editor, $changeRules, $explanation, $logo)) {
        $bounds = $control.RectangleToClient($element.RectangleToScreen($element.ClientRectangle))
        if (-not $control.ClientRectangle.Contains($bounds)) {
            throw "A compact-layout control was clipped: $($element.AccessibleName)"
        }
    }
    $title = @($logo.Parent.Controls | Where-Object Text -eq 'FetchXML Linter')[0]
    if ($logo.Right -gt $title.Left -or $title.Right -gt $changeRules.Parent.Left) {
        throw 'The compact header logo, title and commands overlap.'
    }
    $toggle = @($all | Where-Object AccessibleName -eq 'Show technical details')[0]
    $toggle.Checked = $true
    [System.Windows.Forms.Application]::DoEvents()
    $technical = @($all | Where-Object AccessibleName -eq 'Technical evidence and coverage')[0]
    if (-not $technical.Visible -or $technical.Text -notmatch 'SHA-256:') {
        throw 'Progressive technical details did not reveal source evidence.'
    }
    Write-Output 'Host check passed: findings/details, rule selection, CSV, embedded core, 10,001-user local filtering/hidden selection, task navigation, connection callback, KQL, compact layout and disposal.'
}
finally {
    if ($form) { $form.Dispose() }
    elseif ($control) { $control.Dispose() }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
