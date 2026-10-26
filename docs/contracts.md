# Input and report contracts

## Shortlist CSV v1

```csv
SchemaVersion,OrganizationId,UserId,UserIdType,ViewId,Attribution,EntityName,WindowStartUtc,WindowEndUtc,SampleCount,P95Ms,MetricKind
```

Headers are case-sensitive, may appear in any order and must appear exactly
once with no extra columns. UTF-8 BOM, CRLF/LF and standard quoted fields are
supported. Limits: 2,097,152 characters and 10,000 candidate rows.

| Column | Contract |
| --- | --- |
| SchemaVersion | `1` |
| OrganizationId | Nonempty Dataverse organization GUID; must match active connection |
| UserId | Nonempty GUID in the namespace declared by UserIdType |
| UserIdType | `DataverseSystemUserId` or `EntraObjectId` |
| ViewId | Required only for ExactViewMarker; otherwise empty |
| Attribution | `ExactViewMarker` or `UserTableCandidate` |
| EntityName | Optional logical table name, not display label or entity-set name; if empty, offers all owned ordinary views |
| WindowStartUtc / WindowEndUtc | Both present or both empty; ISO UTC strings ending in Z; end must not precede start |
| SampleCount | Optional positive integer; observed telemetry row count |
| P95Ms | Optional finite nonnegative milliseconds; requires count, window and metric kind |
| MetricKind | `PageLoad` or `ServerRequest` when P95Ms is present; otherwise empty |

Exact duplicate rows after cell trimming are counted and removed.
Invalid rows remain visible alongside valid rows; none is selected automatically.
Check only rows whose identity, attribution and table scope are understood.
The importer does not infer a user's identity namespace.

An exact marker does not prove actor ownership. Only matching owned ordinary
definitions are supported. No match means unresolved/shared/system/deleted/
changed/inaccessible, not permission to collect a different view.
For actor-only candidates the user must select proposed definitions explicitly.
Multiple distinct observations can legitimately reference the same definition;
their provenance is retained rather than combined.

See `examples\shortlist.csv` for synthetic input. Replace organization and actor
IDs with authorized values before connected use. This example is deliberately
not associated with a real environment.

## Full JSON report v1

The envelope contains `SchemaVersion`, `GeneratedAtUtc`, collection outcomes
and definition results. Each definition has source name, analysis UTC timestamp,
optional view/owner/modified IDs, optional original candidate and the analysis.
The candidate's `ResolvedDataverseUserId` is separate from original `UserId`
and `UserIdType`. Analysis contains engine version, status, FetchXML source
hash, root/referenced tables, findings, unevaluated checks and parse errors.
Enums serialize as names. Raw FetchXML and layout XML are not exported.

`Complete`, `Incomplete` and `Invalid` are distinct. `Complete` covers only
implemented checks, not all Dataverse validity/performance questions.
Collection failures/cancellation must be considered alongside selected results.
These are alpha contracts and may change before preview.

## Findings CSV

This is a report, **not a shortlist input format**. It contains one row per
finding with source/status/hash/rule/category/severity/location/evidence/
explanation/recommendation/limitation/documentation. Definitions with no findings
still produce a status row. Use JSON for full metadata/coverage gaps, collection
outcomes and telemetry identity/window/sample provenance.

Observed telemetry timings stay separate from static findings. Page-load p95
is not SQL duration, server-request p95 is not a saved-view benchmark, and
neither is a measured benefit from a recommendation.
