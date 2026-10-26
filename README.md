# FetchXML Linter

A deterministic, read-only **XrmToolBox** tool for reviewing FetchXML and
personal-view definitions. The reusable `FetchXmlLinter.Core` library has no
Dataverse, UI, telemetry-service or AI dependency.

**Status: private development alpha, not a public preview.** The repository
will become public only after the preview gates are met and the owner approves
the visibility change. There is no NuGet publication or Tool Library listing yet.

## What it does

| Input | Workflow |
| --- | --- |
| Manual FetchXML | Paste XML or open a file; optionally paste layout XML. Analyse offline, or explicitly request connected metadata. |
| Personal views | Find enabled Read/Write users by name prefix or GUID, check users, collect owned ordinary views, then check individual views to analyse. |
| Telemetry shortlist | Run a supplied KQL template externally, export CSV, validate/import against the connected organization, check candidates, and select retrieved views. |

Findings include stable rule IDs, source locations, evidence, explanations,
review suggestions, documentation links and limitations. Export the full
analysis as JSON or a lightweight findings CSV.

**It does not execute the supplied FetchXML, edit a saved view, rewrite XML,
benchmark a query, or call an AI service.** Application Insights authentication
and querying remain outside the tool. Connected collection performs only
definition, identity, context-verification and metadata reads.

`Incomplete` means checks could not be evaluated, not that a query is healthy.
`Complete` means the implemented checks ran with their required information;
it does not mean full FetchXML schema validation or proven performance.
Contextual suggestions require business review.

## Build and try locally

Requires Windows, the .NET 9 SDK, .NET Framework 4.8 runtime and Windows
PowerShell 5.1. Framework targeting assemblies restore through NuGet.

```powershell
.\scripts\Build.ps1
```

This builds the solution, runs the tests and disconnected host check, then
creates an **unpublished** alpha package under `artifacts`.
For a development installation, close XrmToolBox and copy only
`src\FetchXmlLinter.XrmToolBox\bin\Release\net48\FetchXmlLinter.XrmToolBox.dll`
into its `Plugins` directory. Use XrmToolBox 1.2025.10.74 or later with an
OAuth connection for connected workflows. Do not copy development-output host
DLLs into an existing installation. The core is embedded in the plugin DLL.

Start disconnected with `examples\manual-fetch.xml`. Offline metadata checks
will be explicitly incomplete. Connected collection requires appropriate
privileges; see [permissions and architecture](docs/architecture.md).

## Documentation

- [Rules and their limitations](docs/rules.md)
- [CSV and result contracts](docs/contracts.md)
- [Application Insights KQL handoff](docs/telemetry.md)
- [Architecture, permissions and privacy](docs/architecture.md)
- [Milestones and preview gates](docs/roadmap.md)
- [Development and contribution](CONTRIBUTING.md)
- [Third-party dependencies](THIRD-PARTY-NOTICES.md)

Examples are synthetic. Do not commit customer definitions, telemetry exports,
connection files, tokens, identities or environment inventories.
MIT covers this repository's original source, not separately licensed host
or SDK dependencies.
