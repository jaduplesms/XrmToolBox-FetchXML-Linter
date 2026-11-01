# Rule catalog

Engine version: `0.1.0`. Rules have stable `FXL` (FetchXML Linter)
identifiers, independent of their topic grouping. Rule version is `1`.
Findings distinguish documented risks from contextual review; severity is not
a numerical confidence score or a predicted performance gain. Structural
parse failures appear in the analysis status/error. Selected structural checks
can also produce Error/Warning findings; `Complete` means evaluation completed,
not that the query is valid or free of findings.

The default analysis surface is `OnlineQuery`. `OfflineProfile` restricts the
requested rules to FXL002-004, FXL010-011 and FXL026, recording the remaining
IDs as skipped. Parsing and safety checks still run. Offline filter results
remain `Incomplete` because relationship scope, Filter Columns, downloaded
row volume, device SQL and sync benefit are outside static-filter analysis.
An all-column projection is not evidence that every column syncs offline.

Dataverse automatic query optimization is enabled by default. Shape
simplification comes first. Late materialization is situational and can slow
simple queries. SQL `options` require Microsoft Support guidance; `no-lock`
is legacy and unnecessary. This tool never adds hints or rewrites XML.

| ID | Detection | Requirement / limitation |
| --- | --- | --- |
| FXL001 | Explicit all-column projection or implicit root-entity projection | XML only. Does not assume attribute-free links project every column. Individual all-column checks remain unevaluated. |
| FXL002 | Leading wildcard for `like`, or `ends-with` | Operator-aware. Ignores `%` in equality and bracket-escaped wildcard literals. Changing to prefix/exact search changes results. |
| FXL003 | Filter on a metadata-confirmed calculated/formula column | Unknown calculation classification is incomplete. A stored alternative needs business review. |
| FXL004 | Filter on Memo or String configured over 850 characters | Requires type/maximum-length metadata. Dataverse Search is not a drop-in equivalent. |
| FXL005 | Explicit projection of a Memo column | Contextual information. Reports supplied layout-cell evidence when matched; body text may be required. |
| FXL006 | Choice label ordering | Requires metadata. Suppressed for `useraworderby=true`/`1`; labels versus integers have different ordering semantics. |
| FXL007 | Lookup or linked-table ordering | Contextual information. Changing sort affects result order and paging. |
| FXL008 | Existing `options`, `latematerialize`, `no-lock` or filter `hint` | Inventory only. SQL options require Microsoft support guidance. No hint is added or removed. |
| FXL009 | More than 15 link-entity elements | Error against the documented join limit; no runtime cost inferred. |
| FXL010 | Two or more joins, with type breakdown | Information only. Two is a review trigger, not a Microsoft performance threshold. |
| FXL011 | Nested link-entity chains | Information only. Reports maximum nesting; depth alone does not prove inefficiency. |
| FXL012 | Non-projecting leaf inner join | Candidate to review existence-only semantics, not an unused join. Excludes intersect links, sorts, nested joins and external alias references. |
| FXL013 | Outer alias referenced outside its link | Review conditions/null semantics and ordering. Null anti-joins can be intentional; no conversion to inner. |
| FXL014 | OR conditions across resolvable table contexts | XML only. Considers different aliases of the same table separately. Existing union filters/ancestors suppress the suggestion. Benefit of union is unmeasured. |
| FXL015 | Distinct combined with joins | Review multiplicity; XML cannot prove duplicates or justify removing distinct. |
| FXL016 | Distinct without explicit ordering | Warning: explicit suitable ordering is required for consistent paging. |
| FXL017 | Multiple sort keys | Information only. Preserve necessary unique tie-breakers. |
| FXL018 | Top combined with count, page or returntotalrecordcount | Structural error; checks attribute presence, including a false count request. |
| FXL019 | Nonpositive/noninteger top, count, page or aggregatelimit; top over 5,000 | Structural error. Does not treat a valid page size as actual result volume. |
| FXL020 | Explicit total-count request | Review only. A model-driven grid may require it. |
| FXL021 | Union hint on a non-OR filter | Structural error. Never change AND to OR merely to satisfy the hint. |
| FXL022 | More than one union hint | Structural error against the documented restriction. |
| FXL023 | Union beyond three filter levels | Warning: documented as ignored. Root filter is level one. |
| FXL024 | Explicit logical-column selection | Summarized metadata evidence; unknown IsLogical is incomplete. No excessive-column threshold assumed. |
| FXL025 | Explicit calculated/formula selection | Summarized metadata evidence; unknown calculation classification is incomplete. Aggregate/group-by columns excluded. |
| FXL026 | Different metadata type families for explicit join keys | Requires both column definitions. Lookups and uniqueidentifier keys share a GUID family; choices and integers share an integer family. Does not validate all possible join constructs. |
| FXL027 | Potential parent-to-child fan-out | Requires a parent primary key and a child lookup targeting that parent table. Applies to inner/outer links. Actual matching rows remain unknown. |
| FXL028 | Explicit lookup selection | Summarized metadata evidence, not proof any lookup is unnecessary. |
| FXL029 | SQL option absent from published list | Support review, not a claim that the option is rejected/ignored. All SQL options require support guidance. |

## Evidence versus heuristics

The structural 15-link limit is documented. Multiple joins, nested joins and
multiple sort keys are deliberately Information-level review notes, not
unsupported "Critical" thresholds. Logical/calculated/lookup projections are
summarized once per rule rather than generating a warning for every column.
Severity (Information/Warning/Error) is separate from finding category
(Structural/DocumentedRisk/ContextualReview); support-only guidance is conveyed
in the explanation and action, not disguised as an impact severity.

The query-shape summary records explicit column count, all-column selections,
join types, maximum depth, sort count and distinct. Projection metadata counts
cover explicit nonaggregate/non-group-by columns and are null when unknown or
not evaluated. Counts do not establish table size, selectivity or execution cost.

## Selecting rules

**Analysis rules** lists the catalog, requirements, limitations and Microsoft
guidance. All 29 rules are enabled by default. Choose a subset, **Select
all**, or **Clear all**; at least one rule must be selected before analysis.
The selection applies to manual queries and collected views for this tool
session only. XML parsing, safety and unsupported-shape coverage always run.

Only selected rules produce findings and rule-specific metadata gaps. FXL003
through FXL007, FXL024 through FXL028 require connected column metadata for
their relevant nodes. Other rules work from XML and avoid connected metadata
requests when selected alone. Implicit join keys remain unevaluated for
FXL026/FXL027 rather than being called invalid: Dataverse can infer some
relationships. XML shape counts are recorded regardless of rule selection.
`Complete` describes the selected scope, not omitted checks. Results and
exports retain the selected rule IDs from the time of analysis.

Every finding includes indexed XML location, line, evidence, explanation,
recommendation, limitation and documentation URL. Source SHA-256 identifies the
exact supplied FetchXML bytes encoded as UTF-8; it is not an execution-plan hash.

Missing metadata, ambiguous aliases, unsupported elements, aggregate/group-by
semantics, alias-based ordering not resolved by the engine and unexpanded
all-column projection produce `Incomplete`. No-findings and `Complete` never
prove a query is fast. Join selectivity, security costs, data distributions,
SQL plans, execution hints' effect and business intent are not inferred.
Attribute-free joins are not automatically unused. Missing paging in a saved
view is not a finding: the caller can supply it. Aggregation opportunities,
business-unused columns, unnecessary outer joins and actual excessive result
sets need consumer/data evidence and are not invented from XML alone.

Rules derive from Microsoft's [query anti-patterns](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/query-antipatterns),
[FetchXML performance guidance](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/optimize-performance),
[column selection](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/select-columns)
and [ordering documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/order-rows),
[joins](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/join-tables),
[link definitions](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/reference/link-entity),
[fetch attributes](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/reference/fetch)
and [paging](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/page-results).
No automatically optimized XML is generated. The editor can indent and
syntax-colour a local copy without changing saved definitions.
