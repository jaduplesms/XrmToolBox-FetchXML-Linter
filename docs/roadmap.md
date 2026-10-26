# Milestones and preview gates

Repository: `jaduplesms/XrmToolBox-FetchXML-Linter`. Display name: **FetchXML
Linter**. License: MIT for original source. Repository visibility: **private
until preview**; no automatic visibility change or publication.

| Milestone | Scope | Current evidence / exit gate |
| --- | --- | --- |
| M0 - Foundation | Repository, contracts, architecture, backlog, CI | Repository and source baseline created; CI must pass remotely |
| M1 - Manual linter | Independent deterministic engine, manual XML, evidence/results | Synthetic rule/parser/CSV tests and disconnected host check; no runtime-performance claims |
| M2 - Personal views | Verified isolated impersonation, paging, selection, metadata, cancellation | Fake-service coverage exists; real OAuth XrmToolBox collection, connection switch and cancellation acceptance still required |
| M3 - Telemetry handoff | KQL templates, typed CSV preview/import and result exports | Synthetic importer coverage exists; authorized live export/import and UI acceptance still required |
| M4 - Public preview | Assets, dependency/privacy review, package, docs and Tool Library preparation | All gates below; owner approval before making repository public or publishing |

## Preview checklist

- [ ] CI runs successfully from a clean Windows runner.
- [ ] Install only the plugin DLL/package in a clean supported XrmToolBox.
- [ ] Verify offline manual flow, resizing, accessibility and export rendering.
- [ ] Verify selected-user and selected-view collection with real OAuth.
- [ ] Verify no-role/no-access, empty, partial, cancelled and throttled coverage.
- [ ] Switch connections during work; verify state reset and no stale results.
- [ ] Resolve both identity namespaces and reject cross-organization shortlist.
- [ ] Run both KQL templates in an authorized workspace; check correlation,
      identity fields, CSV units and imported provenance.
- [ ] Verify JSON/CSV exports against selected inputs and sensitive-data policy.
- [ ] Review supported/unsupported FetchXML constructs and false positives.
- [ ] Complete dependency/license/vulnerability and repository privacy reviews.
- [ ] Obtain approved small/large tool icons and store metadata.
- [ ] Confirm package ID availability, versions, host dependency and clean load.
- [ ] Obtain owner approval to make the repository public at preview.
- [ ] Publish preview package and submit listing only after explicit approval.

No direct Azure authentication, saved-view writes, rewritten XML, query
execution, benchmarking or agentic functionality is scheduled for v1.
GitHub milestones/issues track the remaining acceptance work, not guaranteed dates.
