# Contributing

Discuss material rule, contract or UX changes before implementation.
Keep changes surgical and use only synthetic fixtures in this repository.
Never attach credentials, customer FetchXML/layout, inventories, raw telemetry,
user/environment IDs or exported connection settings to issues or pull requests.

Build and verify on Windows using `.\scripts\Build.ps1`. The core tests run on
.NET 9; SDK collection tests run on .NET Framework 4.8 with a fake read-only
service. The Windows PowerShell host check uses a disconnected WinForms control
and proves the embedded core/resource load path. None replaces live XrmToolBox
and authorized sandbox/workspace acceptance.

For a rule change, add focused positive/negative cases, preserve source evidence,
state metadata prerequisites and semantic limitations, and update the catalog.
Do not silently claim unsupported constructs or missing metadata are healthy.
Do not add query execution, automatic rewrites or saved-record writes.
Keep the core independent of host/SDK/authentication libraries.

`scripts\Build.ps1` creates an unpublished package only. No script publishes to
NuGet, submits a Tool Library listing or changes repository visibility.
Review dependency licenses and API/runtime compatibility before updates.
