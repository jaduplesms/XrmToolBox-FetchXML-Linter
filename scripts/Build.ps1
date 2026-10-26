$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build .\FetchXmlLinter.sln --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet test .\FetchXmlLinter.sln --configuration Release --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    powershell.exe -NoProfile -STA -File .\scripts\Test-Host.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Host check failed.' }
    dotnet pack .\src\FetchXmlLinter.XrmToolBox\FetchXmlLinter.XrmToolBox.csproj --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Local package creation failed.' }
}
finally {
    Pop-Location
}
