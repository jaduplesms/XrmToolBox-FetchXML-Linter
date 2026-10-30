# FetchXML Linter

**Understand your FetchXML. Review potential risks. Make informed changes.**

FetchXML Linter is a read-only [XrmToolBox](https://www.xrmtoolbox.com/) tool
for Microsoft Dataverse. It helps makers, developers and administrators review
personal views, mobile offline-profile filters and supplied FetchXML against
documented guidance and contextual review checks.

Findings explain **what was detected, why it matters, what to consider and
what cannot be concluded**. Each finding includes a stable rule ID, source
location, supporting evidence and links to Microsoft documentation.

> **Release status: early public preview (0.1.0).** Evaluate recommendations in a non-production
> environment before making changes. The tool does not execute the queries
> being reviewed, modify saved definitions or prove that a query is slow.

## Using the workspace

The left navigation is organized around what you want to review, rather than
a single numbered workflow:

- **Personal views > Browse views**: collect and review personal views belonging
  to one or more users, without importing telemetry.
- **Personal views > Find candidates using telemetry**: optionally prepare KQL,
  import its result CSV and retrieve matching views for review. The numbered
  steps belong only to this telemetry-assisted route.
- **Offline profiles**: inspect a mobile offline profile's tables, review its
  configured filters and scope, and inspect generated definitions separately.
- **FetchXML query**: paste FetchXML, open an XML file or review a local copy
  opened from a personal view or offline-profile filter.
- **Analysis rules**: choose which checks run in the next analysis. This is a
  shared setting, not a workflow step.

Start with the task that matches your goal. KQL and CSV are not prerequisites
for direct personal-view collection, offline-profile review or manual analysis.

The tool includes **29 selectable rules** covering column selection, filter
patterns, ordering, joins, distinct results, paging constraints and query
hints. Some checks require Dataverse column metadata; others work without a
connection. See the [rule catalog](docs/rules.md) for individual requirements
and limitations.

## Requirements

- Windows with .NET Framework 4.8.
- XrmToolBox **1.2025.10.74 or later**.
- An authorized Dataverse OAuth connection for personal-view collection,
  offline-profile inspection or metadata-enriched analysis.
- Separate read/query access to the relevant Application Insights resource
  only if you use the optional telemetry workflow.

You can analyse supplied FetchXML without connecting to Dataverse. The
.NET SDK is needed only when building from source.

## Installation

Once the tool is approved and listed, install **FetchXML Linter** through
XrmToolBox's **Tool Library**. Publication and catalog approval are separate
from preparing a release candidate.

For manual installation, use the distributable plugin DLL from a release
package, when available. If no downloadable release is available, follow
[Build from source](#build-from-source).

1. Close all XrmToolBox instances.
2. Locate the **active XrmToolBox data directory** and its `Plugins` folder.
   The default location is `%APPDATA%\MscrmTools\XrmToolBox\Plugins`.
   A custom data directory, including one set through `/overridepath`, takes
   precedence.
3. Copy `FetchXmlLinter.XrmToolBox.dll` from the distribution into that folder.
   For an update, back up the existing plugin outside `Plugins` first.
4. Restart XrmToolBox and open **FetchXML Linter** from the tools list.
5. Connect to Dataverse when using a connected workflow.

**Install only the distributable DLL.** It contains the analysis core; a
separate `FetchXmlLinter.Core.dll` is not required. Do not copy development
output, SDK assemblies or XrmToolBox host dependencies into an existing
installation.

## Quick start: analyse a query

1. Open **FetchXML query**.
2. Paste FetchXML or choose **Open XML file...**.
3. Optionally select **Include layout XML** and supply the corresponding layout.
4. Use **Format XML** for an indented, syntax-coloured local copy.
5. Choose **Analyse query** and select a finding to read its explanation.

Try this synthetic example without a connection:

```xml
<fetch>
  <entity name="account">
    <attribute name="name" />
    <filter>
      <condition attribute="name" operator="like" value="%sample" />
    </filter>
  </entity>
</fetch>
```

The leading-wildcard filter is a review candidate. Replacing it with an exact
or prefix match changes which records qualify, so any change must preserve the
intended business behavior.

Select **Use connected metadata** when you want metadata-dependent checks.
Without the necessary metadata, those checks are reported as unevaluated,
not silently treated as passing.

## Review personal views

### Browse views directly

1. Open **Personal views** and choose **Browse views**.
2. Choose **Load users**.
3. Optionally filter the loaded directory by name, email address or Dataverse
   user ID, then **check** the users whose views you want to collect.
4. Choose **Load Views**.
5. In the combined view list, **check** the views to review and choose
   **Analyse**.
6. Select an analysed view in the results dropdown to inspect its findings.

**Multiple users are supported.** Load Views collects owned ordinary personal
views for every checked user, including checked users hidden by the local
filter. Selecting users or loading definitions does not start analysis:
Analyse reviews only the checked view rows.

The directory includes enabled Read/Write users. It is not an inventory of
every application account, disabled user or access mode. Collection targets
views owned by the selected users; it is not a shared-view or system-view
inventory.

View rows display the owner's name, with the owner GUID as a fallback when a
name is unavailable. Analysed personal-view results use
**View name | User name | Finding count** so that same-named views from different
users remain distinguishable. Permission failures and incomplete collection are reported
explicitly. A failed user is not counted as having no views; inspect
**Technical details** for the collection log.

To inspect or edit a local copy, check exactly one view and choose **Open XML**.
Highlighting a row alone is not sufficient. The copy opens in **FetchXML query**
without changing the saved view or automatically analysing it.

### Find candidates using telemetry

This optional route helps prioritize a review; it does not benchmark saved views.

1. Choose **Find candidates using telemetry > 1. Prepare KQL**.
2. Select a template and use **Copy KQL**.
3. Run it externally in **Logs on the intended Application Insights resource**.
   Confirm the resource belongs to the Dataverse environment you will connect
   to, and verify that the template's identity and correlation assumptions
   match your telemetry.
4. Export the query's **result rows** as CSV, preserving the required headers.
5. Open **2. Import CSV**, choose **Import CSV...**, review validation messages
   and check the candidates to retrieve.
6. Choose **Load matching views**. In **3. Review views**, check the returned
   definitions and choose **Analyse**.

The tool does not connect to Azure Monitor or authenticate to Application
Insights. Importing a CSV does not independently establish its source
environment. Exact view markers still require an ownership match; user/table
observations suggest definitions for review rather than proving which view
caused a delay.

Read the [telemetry guide](docs/telemetry.md) before using this route. The
[CSV contract](docs/contracts.md) defines accepted columns, identity types,
timestamps and measurement units. The supplied KQL targets the Application
Insights table schema, not the differently named Log Analytics workspace tables.

## Review mobile offline profiles

1. Open **Offline profiles** and choose **Load profiles**.
2. Select a profile and choose **Load tables**.
3. Highlight a table to inspect **Configured FetchXML**, **Scope & columns**
   and any **Referenced view SQL**.
4. Check the tables to review and choose **Analyse tables**.
5. Select an analysed filter in the results dropdown to inspect its findings.

**Highlighting and checking have different purposes:** the highlighted table
controls inspection; checked tables define the analysis set. Nothing is checked
automatically. Use **Check all** or **Uncheck all** to change the selection;
**Analyse tables** is below the table checklist.

### What you can inspect

- **Configured FetchXML**: the table item's explicit filter. When there is no
  configured filter and a referenced system view is available, the tool reads
  that view's FetchXML instead and identifies the source.
- **Scope & columns**: distribution criteria, related-record and ownership
  settings, the configured sync interval, raw Filter Columns and raw relationship
  configuration, plus the profile's managed and validated flags.
- **Referenced view SQL**: the stored view-query text, when present. It is
  displayed for inspection, not executed.
- **Generated definitions**: separately loaded diagnostic FetchXML, outer
  FetchXML and offline SQL for the highlighted table.

Offline analysis uses a conservative subset of applicable rules. It does not
apply online-query projection, paging or hint advice indiscriminately to a
mobile filter. Analysis uses the configured filter first, or the referenced
view's FetchXML when available. Items with neither remain visible as coverage
gaps rather than successful zero-finding results. Missing or inaccessible
referenced definitions are reported rather than inferred.

### Inspect generated definitions

1. Highlight a table in the profile's table list.
2. Choose **Load generated definitions** to retrieve diagnostic records for
   that table only, not every checked table.
3. Open **Generated definitions**, select a generated record and inspect its
   **FetchXML**, **Outer FetchXML** or **Generated offline SQL** tab.

This action reads existing generated records; it does not generate, refresh or
publish an offline profile. It is not required before **Analyse tables**.

Generated definitions are diagnostics only: they are not executed or passed
to configured-filter analysis. Empty fields and placeholders can occur, and
there may be no generated records to inspect. Captured SQL is not a guarantee
of the exact statement executed on a device.

### Open a filter for local review

Check exactly one table with explicit FetchXML and choose **Open filter XML**.
The local copy opens in **FetchXML query**, retaining its offline rule scope
and source context.

Relationship-based inclusion and Filter Columns can affect what reaches a
device. Static filter analysis cannot establish downloaded row counts, complete
relationship scope, device execution cost or synchronization improvements.
Offline results retain these limitations and are reported as **Incomplete**.

There are no actions to save, clone, publish or assign a profile.

## Understand the results

The shared results pane identifies the selected result as **Personal view**,
**Offline profile filter** or **Supplied FetchXML**. Select a result in the
dropdown, then select a finding to see its evidence, explanation, suggested
action and limitations.

| Analysis status | Meaning |
| --- | --- |
| **Complete** | The selected implemented checks ran with their required information. This is not a performance certification or full FetchXML schema validation. |
| **Incomplete** | Some checks or parts of the analysis scope could not be evaluated. Review the coverage gaps before drawing conclusions. |
| **Invalid** | Parsing or a required structural validation failed. Review the error details before proceeding. |

Findings use **Information**, **Warning** or **Error** severity. Severity is
not a predicted speed improvement. Multiple joins or nested joins, for example,
can be intentional and do not by themselves prove poor performance.

**Technical details** shows additional evidence, query shape, source hashes,
unevaluated checks and collection outcomes. No findings does not prove that a
query is fast or correct for its intended use.

### Choose the analysis scope

Open **Analysis rules** to inspect each rule's purpose, prerequisites,
limitations and Microsoft guidance. All 29 rules are enabled initially.
Select at least one rule for the next analysis; parsing and safety checks
always run.

Rule selection applies to subsequent analyses. Existing results retain the
selection used when they were created. Offline analysis records which selected
rules were skipped because they are not applicable.

### Work with local copies

Formatting and edits affect only the local editor. Analysing an edited copy
uses its current contents; analysing a collected view uses the saved definition
that was retrieved.

**New query** clears the editors and returns to supplied-FetchXML/online-query
scope. Opening a file or personal view also uses online-query scope.
Simply navigating between workspaces does not change an offline copy's scope
or relabel existing results.

## Export reports

- **Export JSON...** saves all current results with fuller coverage,
  query-shape information, selected rules and source provenance.
- **Export CSV...** saves findings in a tabular report. Definitions without
  findings still receive a status row.

An exported findings CSV is **not** a telemetry shortlist CSV and cannot be
used in its place. See [Input and report contracts](docs/contracts.md).

**Review and sanitize every report before sharing.** Although raw FetchXML
and layout XML are omitted from these exports, names, identifiers, evidence,
filter values, profile settings and service error messages can still disclose
personal or confidential information.

## Permissions and data handling

The tool uses the active XrmToolBox connection for authorized definition,
identity, context-verification and metadata reads. XrmToolBox manages
authentication; the tool does not maintain its own credential store.

Personal-view collection uses an isolated connection to impersonate each
selected owner, verifies the effective context before collection and restores
the caller afterward. It requires the directly assigned **Act on Behalf of
Another User** privilege and applicable read permissions. The verification
step also requires the target user to be able to read user records. Being
enabled is not evidence that these permissions exist.

Offline-profile inspection uses the connected caller without impersonation.
Access to the relevant profile, table-item, generated-filter and referenced
view definitions is subject to that caller's permissions.

FetchXML Linter does **not**:

- Execute the supplied query, generated SQL or a saved view's business-data query.
- Modify views, profiles, security roles or environment settings.
- Automatically rewrite or optimize queries.
- Measure execution plans, query duration or synchronization improvements.
- Call an AI service or submit definitions to an external analysis service.

Connected operations still send authorized read requests to Dataverse.
Cancellation is cooperative and may need to wait for an in-flight read.
Changing connections clears connection-bound state and prevents stale results
from replacing current work.

Use only authorized data. Do not attach real queries, telemetry, connection
files, tokens, user details or environment identifiers to public issues.
Provide a minimal synthetic example instead.

For implementation details, see [Architecture, permissions and privacy](docs/architecture.md).

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| The tool does not appear or reports a missing Core assembly | Close XrmToolBox and install the merged distribution DLL into the active data directory's `Plugins` folder. Do not use the unmerged development DLL. |
| An action is disabled | Check the required items. Opening XML requires exactly one checked source; highlighting alone does not count. |
| A user's views cannot be collected | Review the collection log and caller/target permissions. A failed context check prevents collection for that user. |
| Metadata-dependent checks are incomplete | Connect to the intended environment, enable connected metadata for supplied queries and review any reported metadata-read failures. |
| A CSV is rejected or returns no matching view | Check the shortlist schema, environment, identity namespace, ownership and table scope. System, shared, deleted or inaccessible views are not substituted with unrelated views. |
| An offline item has no configured FetchXML | Review its criteria and referenced-view information. Absence of explicit XML is a coverage limitation, not evidence that no data is synchronized. |
| Results remain unchanged after editing or changing rules | Run the appropriate Analyse action again. Existing results describe the earlier analysis. |

Before applying a recommendation, preserve business semantics and compare
behavior and performance in an appropriate test environment. Dataverse already
performs automatic query optimization. Query hints are not a first-line fix;
SQL `options` require Microsoft Support guidance.

## Build from source

Building requires Windows, the **.NET 9 SDK**, .NET Framework 4.8 and
Windows PowerShell 5.1. From the repository root, run:

```powershell
.\scripts\Build.ps1
```

The script builds the solution, runs automated tests and disconnected host
checks, audits dependencies against the configured package feeds, and creates
and verifies a local package under `artifacts`. A successful feed-scoped audit
is not a guarantee that dependencies have no vulnerabilities. The script does
not publish the package or install the tool.

The installable merged DLL is:

```text
src\FetchXmlLinter.XrmToolBox\bin\Release\net48\plugin\FetchXmlLinter.XrmToolBox.dll
```

The DLL in the parent `net48` output directory is for development and tests,
not installation. Follow the [installation steps](#installation) after building.

## Documentation and contributions

- [Rule catalog and limitations](docs/rules.md)
- [Application Insights telemetry workflow](docs/telemetry.md)
- [Input and report contracts](docs/contracts.md)
- [Architecture, permissions and privacy](docs/architecture.md)
- [Contribution guidance](CONTRIBUTING.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

For a reproducible issue, include the tool and XrmToolBox versions, analysis
workflow, relevant rule IDs and a sanitized or synthetic example. Remove
identifying details from screenshots, logs and reports before submitting them.

## License

The project's original source is available under the [MIT License](LICENSE).
XrmToolBox and SDK dependencies retain their respective licenses; see
[Third-party notices](THIRD-PARTY-NOTICES.md).
