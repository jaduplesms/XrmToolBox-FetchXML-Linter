# Roadmap

FetchXML Linter is early-preview software. This roadmap describes current capabilities
and evaluation priorities, not committed delivery dates or a guarantee of
support for every Dataverse deployment.

## Current capabilities

- Read-only analysis of supplied FetchXML, selected personal views and
  configured mobile offline-profile filters.
- A catalog of 29 selectable rules with evidence, limitations and Microsoft
  documentation links.
- Multi-user personal-view collection with verified impersonation, explicit
  ownership scope and visible collection failures.
- Optional Application Insights KQL-to-CSV handoff for prioritizing review.
- Local XML inspection and formatting, shared results and JSON/CSV reports.
- Separate inspection of offline scope, Filter Columns and generated definitions.

## Evaluation priorities

- Connected collection, cancellation and connection changes across supported
  OAuth configurations.
- Permission failures, inaccessible definitions and partial-coverage reporting.
- Telemetry identity mappings, correlation assumptions and CSV compatibility
  across deployment-specific schemas.
- Offline-filter interpretation, relationship-scope limitations and clear
  separation of configured and generated definitions.
- Accessibility, compact layouts and report readability.
- Clean installation, dependency compatibility, license review and privacy
  review before broader distribution.
- Rule accuracy and unsupported-shape coverage using synthetic reproducible cases.

## Product boundaries

The current scope does not include query execution, automatic query rewriting,
saved-view or profile changes, direct Azure Monitor authentication, performance
benchmarking or AI-based analysis.

Recommendations remain advisory. Changes to a query's behavior require
business review and validation in an appropriate test environment.

See [Contribution guidance](../CONTRIBUTING.md) for reporting issues and
proposing changes without sharing confidential or identifying data.
