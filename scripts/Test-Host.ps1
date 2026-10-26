param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this .NET Framework host check with Windows PowerShell 5.1 and -STA.'
}
$root = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $root "src\FetchXmlLinter.XrmToolBox\bin\$Configuration\net48"
$pluginPath = Join-Path $runtime 'FetchXmlLinter.XrmToolBox.dll'
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
    $assembly = [System.Reflection.Assembly]::Load([IO.File]::ReadAllBytes($pluginPath))
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
    $all = @(Get-Descendants $control)
    $editor = @($all | Where-Object AccessibleName -eq 'FetchXML')[0]
    $button = @($all | Where-Object AccessibleName -eq 'Analyse XML')[0]
    $list = @($all | Where-Object AccessibleName -eq 'Analysis results')[0]
    $editor.Text = "<fetch><entity name='account'><attribute name='name'/><filter><condition attribute='name' operator='like' value='%synthetic'/></filter></entity></fetch>"
    $button.PerformClick()
    [System.Windows.Forms.Application]::DoEvents()
    if ($list.Items.Count -ne 1 -or $list.Items[0].Result.Findings.RuleId -notcontains 'FXL002') {
        throw 'Disconnected manual linting did not produce the expected evidence.'
    }
    $core = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'FetchXmlLinter.Core' })[0]
    if (-not $core -or $core.Location) {
        throw 'The core library was not loaded from the plugin embedded resource.'
    }
    $tabs = @($all | Where-Object { $_ -is [System.Windows.Forms.TabControl] })[0]
    if ($tabs.TabPages.Count -ne 4) { throw 'Expected all four input workflows.' }
    $tabs.SelectedIndex = 3
    [System.Windows.Forms.Application]::DoEvents()
    $kql = @($all | Where-Object AccessibleName -eq 'KQL template')[0]
    if ($kql.Text -notmatch 'SchemaVersion') { throw 'The embedded KQL template did not load.' }
    $form.Size = [System.Drawing.Size]::new(850, 650)
    [System.Windows.Forms.Application]::DoEvents()
    Write-Output 'Host check passed: disconnected lint, embedded core, input tabs, KQL resource, resize and disposal.'
}
finally {
    if ($form) { $form.Dispose() }
    elseif ($control) { $control.Dispose() }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
