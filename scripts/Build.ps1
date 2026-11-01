$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build .\FetchXmlLinter.sln --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet test .\FetchXmlLinter.sln --configuration Release --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $auditJson = dotnet list .\FetchXmlLinter.sln package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed; release readiness is unverified.' }
    $audit = ($auditJson -join [Environment]::NewLine) | ConvertFrom-Json
    if ($audit.problems -or -not $audit.projects -or -not $audit.sources) {
        throw 'Dependency audit did not return a complete feed-scoped result.'
    }
    foreach ($project in $audit.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($package.vulnerabilities) { throw "Dependency audit reported vulnerabilities in $($package.id) $($package.resolvedVersion)." }
            }
        }
    }
    Write-Output "No known vulnerable packages reported by configured sources: $($audit.sources -join ', ')"
    powershell.exe -NoProfile -STA -File .\scripts\Test-Host.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Host check failed.' }
    dotnet pack .\src\FetchXmlLinter.XrmToolBox\FetchXmlLinter.XrmToolBox.csproj --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Local package creation failed.' }
    [xml]$properties = Get-Content -LiteralPath .\Directory.Build.props -Raw
    & .\scripts\Test-Package.ps1 -PackagePath ".\artifacts\FetchXmlLinter.XrmToolBox.$($properties.Project.PropertyGroup.Version).nupkg"
}
finally {
    Pop-Location
}
