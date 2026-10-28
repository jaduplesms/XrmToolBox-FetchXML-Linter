# Milestones and preview gates

Repository: `jaduplesms/XrmToolBox-FetchXML-Linter`. Display name: **FetchXML
Linter**. License: MIT for original source. Repository visibility: **private
until preview**; no automatic visibility change or publication.

| Milestone | Scope | Current evidence / exit gate |
| --- | --- | --- |
| M0 - Foundation | Repository, contracts, architecture, backlog, CI | Source baseline persisted privately; clean Windows CI passed |
| M1 - Manual linter | Independent deterministic engine, manual XML, evidence/results | Synthetic rule/parser/CSV tests and disconnected host check; no runtime-performance claims |
| M2 - Personal views | Verified isolated impersonation, paging, selection, metadata, cancellation | Fake-service coverage exists; real OAuth XrmToolBox collection, connection switch and cancellation acceptance still required |
| M3 - Telemetry handoff | KQL templates, typed CSV preview/import and result exports | Synthetic importer coverage exists; authorized live export/import and UI acceptance still required |
| M4 - Public preview | Assets, dependency/privacy review, package, docs and Tool Library preparation | All gates below; owner approval before making repository public or publishing |

## Preview checklist

- [x] CI runs successfully from a clean Windows runner.
- [ ] Install only the plugin DLL/package in a clean supported XrmToolBox.
- [ ] Verify offline manual flow, resizing, accessibility and export rendering.
- [ ] Verify selected-user and selected-view collection with real OAuth.
- [ ] Verify no-role/no-access, empty, partial, cancelled and throttled coverage.
- [ ] Switch connections during work; verify state reset and no stale results.
- [ ] Resolve both identity namespaces and reject cross-organization shortlist.
- [ ] Run both KQL templates in authorized Application Insights Logs; check correlation,
      identity fields, CSV units and imported provenance.
- [ ] Verify JSON/CSV exports against selected inputs and sensitive-data policy.
- [ ] Review supported/unsupported FetchXML constructs and false positives.
- [ ] Complete dependency/license/vulnerability and repository privacy reviews.
- [x] Obtain approved small/large tool icons and store metadata.
- [ ] Confirm package ID availability, versions, host dependency and clean load.
- [ ] Obtain owner approval to make the repository public at preview.
- [ ] Publish preview package and submit listing only after explicit approval.

## Implemented alpha.7 backlog

- Paged enabled Read/Write user directory with optional local name/email/ID
  filtering, hidden-selection preservation, explicit partial coverage and
  contextual Load Views/Open XML/Analyse actions.
- 29 selectable, stable-ID rules covering projection, filters, sorting,
  joins/fan-out, distinct, numeric limits/paging and hint restrictions.
- Query-shape evidence in the UI and JSON, without inferred SQL cost or
  automatic rewriting.
- Prior alpha.6 manifest lookup false negative resolved: parsed
  ScannedAssemblies and PluginMetadata both contain the tool/version.
- Alpha.7 installed locally as assembly 0.1.0.6, with the previous plugin DLL
  backed up. Installed-path smoke and restarted-host discovery registration
  confirmed. No package publication or connected acceptance is implied.

## Alpha.8 branding

The approved original logo is preserved in `assets`, with transparent 32/80-pixel
tool-list icons and a branded tool header. The logo is embedded in the plugin;
no separate installed image files are needed.
Alpha.8 is installed locally as assembly 0.1.0.7, with alpha.7 backed up.
Installed-path smoke and reopened-host discovery confirmed the new version and
both approved icons. No package has been published.

## Alpha.9 workflow and layout

- Numbered KQL/CSV/personal-view/manual-query navigation, with direct entry at
  any step and rules kept as a separate settings page.
- Independently scrolling personal-view selectors, permanently visible actions,
  a default 60/40 input/results split and retained user-adjusted ratios.
- One Load users/Refresh users action, contextual button tooltips and
  exactly-one-checked Open XML with explicit source and destination.
- CSV import before matching retrieval; retrieval requires checked candidates.
- Delayed, cancellable modal progress for background loading and analysis,
  with connection-generation guards against stale results.
- Compact headings, count tooltips and result summaries preserve list/grid
  space without requiring whole-page scrolling.

Alpha.9 was installed locally as assembly 0.1.0.8 after owner approval, with
alpha.8 backed up. Installed-path smoke and reopened-host discovery confirmed
the new version. The local development package remains unpublished.

## Alpha.10 XML readability

FetchXML and Layout XML loaded for inspection are indented and syntax-coloured.
An explicit Format XML action handles pasted/edited content as an undoable local
edit, preserving values, mixed content and whitespace scopes. Formatting is
bounded and prohibits DTDs/external entities. Invalid XML remains available for
inspection and linting; saved definitions are never changed.

Alpha.10 is installed locally as assembly 0.1.0.9, with alpha.9 backed up.
Installed-path smoke, matching source/installed hashes and reopened-host
discovery confirmed the update. The development package remains unpublished.

Local automated checks and packaging do not close the connected acceptance,
licensing or publication gates above.

No direct Azure authentication, saved-view writes, automatic query optimization, query
execution, benchmarking or agentic functionality is scheduled for v1.
GitHub milestones/issues track the remaining acceptance work, not guaranteed dates.

Initial alpha evidence: commit `f9108dd`; the [clean Windows build](https://github.com/jaduplesms/XrmToolBox-FetchXML-Linter/actions/runs/40353835484)
completed the automated tests, disconnected host check and unpublished package
creation. This is not evidence of live connected acceptance.
