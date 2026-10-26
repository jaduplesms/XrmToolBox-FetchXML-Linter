# Initial rule catalog

Engine version: `0.1.0-alpha.1`. Each initial rule has rule version `1`.
Findings distinguish documented risks from contextual review; severity is not
a numerical confidence score or a predicted performance gain. Structural
parse failures appear in the analysis status/error rather than as risk findings.

| ID | Detection | Requirement / limitation |
| --- | --- | --- |
| FXL001 | Explicit all-column projection or implicit root-entity projection | XML only. Does not assume attribute-free links project every column. Individual all-column checks remain unevaluated. |
| FXL002 | Leading wildcard for `like`, or `ends-with` | Operator-aware. Ignores `%` in equality and bracket-escaped wildcard literals. Changing to prefix/exact search changes results. |
| FXL003 | Filter on a metadata-confirmed calculated/formula column | Unknown calculation classification is incomplete. A stored alternative needs business review. |
| FXL004 | Filter on Memo or String configured over 850 characters | Requires type/maximum-length metadata. Dataverse Search is not a drop-in equivalent. |
| FXL005 | Explicit projection of a Memo column | Contextual information. Reports supplied layout-cell evidence when matched; body text may be required. |
| FXL006 | Choice ordering | Requires metadata. Labels versus integers have different ordering semantics. |
| FXL007 | Lookup or linked-table ordering | Contextual information. Changing sort affects result order and paging. |
| FXL008 | Existing `options`, `latematerialize`, `no-lock` or filter `hint` | Inventory only. SQL options require Microsoft support guidance. No hint is added or removed. |

Every finding includes indexed XML location, line, evidence, explanation,
recommendation, limitation and documentation URL. Source SHA-256 identifies the
exact supplied FetchXML bytes encoded as UTF-8; it is not an execution-plan hash.

Missing metadata, ambiguous aliases, unsupported elements, aggregate/group-by
semantics, alias-based ordering not resolved by the engine and unexpanded
all-column projection produce `Incomplete`. No-findings and `Complete` never
prove a query is fast. Join selectivity, security costs, data distributions,
SQL plans, execution hints' effect and business intent are not inferred.

Rules derive from Microsoft's [query anti-patterns](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/query-antipatterns),
[FetchXML performance guidance](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/optimize-performance),
[column selection](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/select-columns)
and [ordering documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/order-rows).
No automatic rewritten XML is generated.
