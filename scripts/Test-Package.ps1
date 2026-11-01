param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
$temporaryDll = [IO.Path]::GetTempFileName()
try {
    $files = @{
        'docs/README.md' = 'README.md'
        'docs/LICENSE' = 'LICENSE'
        'docs/THIRD-PARTY-NOTICES.md' = 'THIRD-PARTY-NOTICES.md'
        'assets/icon-80.png' = 'assets\icon-80.png'
    }
    $dllEntry = 'lib/net48/Plugins/FetchXmlLinter.XrmToolBox.dll'
    $nuspecEntry = 'FetchXmlLinter.XrmToolBox.nuspec'
    $expected = @($files.Keys) + @($dllEntry, $nuspecEntry)
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -notin $expected -and
            $entry.FullName -notmatch '^(_rels/\.rels|\[Content_Types\]\.xml|package/services/metadata/core-properties/[^/]+\.psmdcp)$') {
            throw "Unexpected package content: $($entry.FullName)"
        }
    }
    foreach ($name in $expected) {
        if (@($archive.Entries | Where-Object FullName -eq $name).Count -ne 1) {
            throw "Missing or duplicate package entry: $name"
        }
    }
    foreach ($name in $files.Keys) {
        $memory = [IO.MemoryStream]::new()
        $stream = $archive.GetEntry($name).Open()
        try {
            $stream.CopyTo($memory)
            $actual = [Convert]::ToBase64String($memory.ToArray())
            $source = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $root $files[$name])))
            if ($actual -cne $source) { throw "Packaged content differs from source: $name" }
        }
        finally {
            $stream.Dispose()
            $memory.Dispose()
        }
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry($nuspecEntry).Open())
    try { [xml]$nuspec = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
    $metadata = $nuspec.package.metadata
    foreach ($field in @('id', 'version', 'title', 'authors', 'projectUrl', 'description', 'releaseNotes', 'tags', 'icon', 'iconUrl')) {
        if ([string]::IsNullOrWhiteSpace([string]$metadata.$field)) { throw "Missing package metadata: $field" }
    }
    if ($metadata.tags -notmatch '\bXrmToolBox\b' -or
        $metadata.icon -ne 'assets/icon-80.png' -or $metadata.readme -ne 'docs/README.md') {
        throw 'Package discovery metadata or embedded document paths are invalid.'
    }
    foreach ($field in @('projectUrl', 'iconUrl')) {
        $uri = [Uri]([string]$metadata.$field)
        if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https') { throw "Expected a public HTTPS $field." }
    }
    $dependencies = @($metadata.dependencies.group.dependency)
    if ($dependencies.Count -ne 1 -or $dependencies[0].id -ne 'XrmToolBox' -or
        $dependencies[0].version -ne '1.2025.10.74') {
        throw 'The package must depend only on the supported XrmToolBox host.'
    }
    [xml]$properties = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    if ($metadata.version -cne $version) { throw 'Package version differs from the release source.' }
    $expectedVersion = [version]$version
    $normalizedVersion = [version]::new($expectedVersion.Major, $expectedVersion.Minor, $expectedVersion.Build, 0)
    [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry($dllEntry), $temporaryDll, $true)
    if ([Reflection.AssemblyName]::GetAssemblyName($temporaryDll).Version -ne $normalizedVersion -or
        [version][Diagnostics.FileVersionInfo]::GetVersionInfo($temporaryDll).FileVersion -ne $normalizedVersion) {
        throw 'Package, assembly and file versions are not aligned.'
    }
    Write-Output "Package content, source documents, icon, host dependency and release versions verified: $version"
}
finally {
    $archive.Dispose()
    Remove-Item -LiteralPath $temporaryDll -Force
}
