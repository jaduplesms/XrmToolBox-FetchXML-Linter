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
| Personal views | Load enabled Read/Write users with paging, filter locally by name/email/Dataverse ID, select owners, then load and select their personal views. |
| Telemetry shortlist | Run a supplied KQL template in the intended Application Insights resource, export CSV (no organization GUID required), import into the matching Dataverse connection, check candidates, and select retrieved views. |

Findings include stable rule IDs, source locations, evidence, explanations,
review suggestions, documentation links and limitations. Export the full
analysis as JSON or a lightweight findings CSV.

## Using the workspace

The left navigation follows the optional usage-led workflow: **1. Prepare
usage KQL**, **2. Import usage CSV**, **3. Review personal views**, and
**4. Analyse a query**. Start at any step; KQL and CSV are not prerequisites
for direct personal-view collection or manual analysis. **Settings: Analysis
rules** is separate from the numbered workflow.

**Prepare usage KQL** contains the templates and **Copy KQL**. Run the query
externally in the intended Application Insights resource and export CSV.
**Import usage CSV** places **Import CSV...** before **Load matching views**;
matching retrieval stays disabled until validated candidates are checked.
After retrieving definitions, the tool opens **Review personal views**.

**Review personal views** has one **Load users** action, which becomes
**Refresh users** after a load, beside the optional local filter. There is no two-character prefix or
first-200 limit. Paging reads only names, email addresses and IDs, with a
1,000-page safety guard. Counts distinguish visible, loaded and selected users.
Checked users remain selected when hidden by a filter; **Load Views** includes
those hidden selections. Refresh preserves selected IDs still returned.
Cancelled/failed loads retain received pages and explicitly mark the directory
incomplete. The directory is cached only for this tool session/connection.
The scope remains enabled Read/Write users, not every system/application account.
**Load Views**, **Open XML** and **Analyse** sit beside their associated lists
and are enabled only when the required selection exists. Each list scrolls
independently; the page does not need scrolling to reach its actions.
The adjustable input/results split defaults to **60% / 40%**, preserving a
manually chosen ratio during resizing and navigation. Compact pages omit
introductory headings and shorten the result summary to retain usable lists.
Count labels expose their full text through tooltips when space is limited.

**Open XML** requires exactly one **checked** view, regardless of which row
is highlighted. It opens that view in **Analyse a query**, identifies its
source, and does not run an analysis. **Analyse** reviews all checked views;
choose a view in **Results for** to see its findings.

FetchXML and Layout XML loaded from a view or file are indented and
syntax-coloured. Use **Format XML** after pasting or editing; it formats the
query and, when included, the layout. Formatting is an undoable local edit,
not an optimization or a saved-view update. XML values, mixed content and
`xml:space` are preserved. Invalid/unsafe XML is retained unchanged with an
explanation so it can still be inspected and analysed. Formatting rejects DTDs,
excessive nesting and input/output above the existing character limit.
Manual analysis uses the current editor contents, so its hashes and line
locations describe that local copy; collected-view analysis uses the original
saved definitions.

Action buttons have purpose-specific tooltips. Longer loading, import and
analysis operations show a cancellable modal progress dialog after a short
delay; quick operations do not flash a dialog. Cancellation is cooperative:
an in-flight synchronous Dataverse read may need to finish. Connection changes
invalidate old work so stale completion cannot replace current results.

**Analysis rules** lists all 29 rules with topic labels, requirements, limitations
and Microsoft guidance. All are enabled initially; select a subset for the next
analysis in any workflow. Selection lasts for the current tool session.
XML parsing and safety checks cannot be disabled. At least one rule is required.

Results show a coverage summary, query-shape counts and a selectable findings table. Select a row
for its explanation, suggested action and limitations. **Technical details**
reveals query shape, hashes, evidence, unevaluated checks and the collection log. JSON and
findings CSV exports record the rules selected when each analysis ran; changing
the selection does not relabel earlier results.

**It does not execute the supplied FetchXML, edit a saved view, optimize XML,
benchmark a query, or call an AI service.** Application Insights authentication
and querying remain outside the tool. Connected collection performs only
definition, identity, context-verification and metadata reads.

`Incomplete` means checks could not be evaluated, not that a query is healthy.
`Complete` means the selected implemented checks ran with their required information;
it does not mean full FetchXML schema validation or proven performance.
Contextual suggestions require business review.

Dataverse automatically optimizes queries. Simplify columns, filters, sorts
and joins before experimenting with hints. Late materialization can make
simple queries slower; SQL `options` require Microsoft Support guidance.
`no-lock` is legacy and unnecessary. Join counts and nesting are review
evidence, not a measured cost or a universal slowness threshold.

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
into the active **data directory's** `Plugins` folder, normally
`%APPDATA%\MscrmTools\XrmToolBox\Plugins` (not a new folder beside the EXE).
An `/overridepath` launch argument changes that data directory.
Use XrmToolBox 1.2025.10.74 or later with an
OAuth connection for connected workflows. Do not copy development-output host
DLLs into an existing installation. The core is embedded in the plugin DLL.
If an early development installation has a separate `FetchXmlLinter.Core.dll`
in `Plugins`, close the host and move that obsolete own DLL to a backup outside
the plugin directory. A neighbouring legacy copy can override the embedded
core, leaving old behavior active even when the plugin version is current.
Do not remove host or other tools' dependencies.

To check the installed plugin's actual assembly loading and CSV importer:

```powershell
powershell.exe -NoProfile -STA -File .\scripts\Test-Host.ps1 `
    -InstalledPluginPath "$env:APPDATA\MscrmTools\XrmToolBox\Plugins\FetchXmlLinter.XrmToolBox.dll"
```

Development rebuilds must increment `AssemblyVersion` when replacing an
already-scanned DLL: XrmToolBox caches tool discovery by filename and assembly
version. Do not delete the user's tool manifest to force discovery.

The approved logo appears in the XrmToolBox small/large tool listings and the
tool header. Its unchanged original is kept in `assets\logo-original.png`.
Run `.\scripts\Update-Logo.ps1` to regenerate the transparent 32/80-pixel PNGs
and their compile-time Base64 constants before rebuilding. No loose image files
are required in the installed plugin folder.

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
