# Application Insights shortlist handoff

The tool does not connect to Azure Monitor. XrmToolBox's Dataverse connection
does not supply Azure Logs authorization. Use your authorized Azure Portal
Logs session separately, then import a CSV.

## Application Insights Logs templates

1. Open **Logs on the intended Application Insights resource** with read/query
   access, rather than opening Logs on its linked Log Analytics workspace.
2. Choose `kql\exact-view-markers.kql` or `kql\user-table-candidates.kql`
   (also available under **Personal views > Find candidates using telemetry > 1. Prepare KQL**).
3. Choose a bounded time window. The selected Application Insights resource
   supplies the scope; no organization GUID placeholder is required. These
   templates assume a resource dedicated to the intended Dataverse organization.
4. Verify the deployment's schema, identity namespaces and correlation fields
   before interpreting the results. These are schema-specific starter templates,
   not universal telemetry mappings.
5. Run externally. Export **result rows** as CSV, keeping the exact v1 headers.
   Do not use an export containing KQL source text instead of its result table.
6. Connect the tool to the same Dataverse organization, import, review rejected
   rows and identity types, check candidates, collect definitions, then check
   views to analyse.

`OrganizationId` is not required in the CSV. Candidates are bound to the active
Dataverse connection when imported; the tool cannot independently verify the
telemetry's source organization when the column is absent. Older exports with
an explicit `OrganizationId` are still checked against the active connection.
For a resource containing multiple organizations, add appropriate environment
scoping before exporting instead of assuming a resource is always single-org.

The templates target `pageViews`, `dependencies` and `requests`, using
`timestamp`, `customDimensions`, `duration` and `operation_Id`. This query
surface can be available even when Application Insights stores its data in a
linked workspace. Workspace Logs exposes different table/column names, such
as `AppPageViews`, `TimeGenerated`, `Properties` and `DurationMs`; these
templates are not written for that surface.

UTC CSV timestamps concatenate literal `T` and `Z` outside `format_datetime`;
putting them inside `"yyyy-MM-ddTHH:mm:ss.fffZ"` causes a Kusto format-parse
error. The templates are schema-specific and are not validated against every
deployment. Verify the exported identities, timings and imported scope before
relying on a shortlist.

## Attribution and identity

- **ExactViewMarker:** page view `customDimensions.viewId` identifies the view marker.
  The template requires an operation with dependency
  evidence. It assumes the page view `user_Id` is a Dataverse `systemuserid`:
  verify this before use. System/shared views can produce markers too.
- **UserTableCandidate:** `SDKRetrieveMultiple` dependencies
  supply the logical table; their parent IDs are matched to server request IDs
  within the same operation. The template assumes server request `user_Id` is an
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

Templates filter time early within the selected resource and project only
required join/grouping columns. Start with a short window before widening.
Verify the resource's environment scope. CSV files and reports contain
identifiers and timings: treat them as potentially sensitive local artifacts.
