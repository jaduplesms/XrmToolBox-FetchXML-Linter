# Application Insights shortlist handoff

The tool does not connect to Azure Monitor. XrmToolBox's Dataverse connection
does not supply Azure Logs authorization. Use your authorized Azure Portal
Logs session separately, then import a CSV.

## Workspace-based templates

1. Open the correct linked Log Analytics workspace with read/query access.
2. Choose `kql\exact-view-markers.kql` or `kql\user-table-candidates.kql`
   (also available in the tool's KQL tab).
3. Replace organization GUID and Application Insights `_ResourceId`, and
   choose a bounded time window. Keep the resource and organization scopes.
4. Verify the deployment's schema, identity namespaces and correlation fields
   before interpreting the results. These are schema-specific starter templates,
   not universal telemetry mappings.
5. Run externally. Export **result rows** as CSV, keeping the exact v1 headers.
   Do not use an export containing KQL source text instead of its result table.
6. Connect the tool to the same Dataverse organization, import, review rejected
   rows and identity types, check candidates, collect definitions, then check
   views to analyse.

The templates target workspace tables `AppPageViews`, `AppDependencies` and
`AppRequests`, using `TimeGenerated`, `Properties`, `DurationMs` and `_ResourceId`.
Classic `pageViews`/`dependencies`/`requests` schemas are not supported by these
templates. A customer-neutral live export/import acceptance run remains a
preview gate; do not claim the templates were validated on every deployment.

## Attribution and identity

- **ExactViewMarker:** page view `Properties.viewId` identifies the view marker.
  The template requires an operation with organization-scoped server dependency
  evidence. It assumes the page view `UserId` is a Dataverse `systemuserid`:
  verify this before use. System/shared views can produce markers too.
- **UserTableCandidate:** organization-scoped `SDKRetrieveMultiple` dependencies
  supply the logical table; their parent IDs are matched to server request IDs
  within the same operation. The template assumes server request `UserId` is an
  Entra object ID. Missing correlation produces no candidate, not a guessed
  identity or query. A request can involve multiple tables; the same duration
  can therefore appear in more than one table's candidate group.

Operation IDs alone do not prove which request ran which saved view. A marker
is evidence of the selected view, not proof every operation dependency is
caused by that definition. The tool verifies ownership and never guesses a
view from a user-only observation. It retrieves authoritative definitions from
Dataverse, not XML hidden in telemetry URLs: `fetchXml` URL values can be
redacted placeholders.

## Timing and coverage

P95 is calculated over observed telemetry rows in milliseconds. Page-load and
server-request durations differ and are not SQL query time. Counts are retained
observations, not sampling-corrected usage totals. Inspect sampling/retention/
export configuration; absence is not proof of no usage. Sparse samples and
correlation/export gaps reduce usefulness.

Templates filter time/resource early and project only required join/grouping
columns. Start with a short window before widening. Do not remove environment
scoping to make empty results look successful. CSV files and reports contain
identifiers and timings: treat them as potentially sensitive local artifacts.
