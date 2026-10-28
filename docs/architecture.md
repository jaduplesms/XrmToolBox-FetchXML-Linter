# Architecture, permissions and privacy

`FetchXmlLinter.Core` targets .NET Standard 2.0. It parses bounded XML, accepts
optional column metadata, applies versioned deterministic rules and validates
shortlist CSV. It has no SDK, authentication, UI or service dependency.

`FetchXmlLinter.XrmToolBox` targets .NET Framework 4.8 WinForms and the
XrmToolBox 1.2025.10.74 APIs. It uses the host's existing OAuth connection,
clones the `CrmServiceClient`, and changes caller identity only on that private
client. The shared host client is never impersonated or disposed. The legacy
client type is a host-compatibility choice, not a recommendation for standalone
new Dataverse applications. Only our core DLL is embedded.

## Read-only collection

1. Find users or resolve typed CSV actors with bounded SDK reads.
2. Set the isolated client's caller to the target Dataverse `systemuserid`.
3. Verify effective context with a `systemuser` FetchXML read using `eq-userid`.
   Require exactly one matching ID before reading views. `WhoAmI` is used for
   organization identity only, not to prove impersonation.
4. Read `userquery` definitions with `ownerid=target` and `querytype=0`, paging
   250 definitions at a time. Missing/repeated cookies and the 1,000-page cap
   are explicit failures; discard a failed user's partial collection.
5. Restore caller state after each target. The user explicitly selects views
   before metadata-enriched linting.

The caller needs directly assigned **Act on Behalf of Another User**
(`prvActOnBehalfOfAnotherUser`) and applicable read privileges; effective
privileges remain constrained by the caller/target intersection. The context
guard additionally requires the target to read `systemuser`. Enabled Read/Write
users can still lack roles or permissions. Those failures are coverage gaps,
not successful zero-view results. The tool never changes roles or access mode.

Connection changes cancel the operation, invalidate its generation and clear
collected/shortlist/results state. Completed stale work is discarded. Cancellation
is cooperative between reads and in retry waits; it cannot abort an in-flight
synchronous SDK call. Known service-protection faults receive at most two
additional tool-level attempts; SDK-internal retries can also occur.

Metadata is retrieved once per distinct referenced table within an analysis
operation, not cached across connections. Unknown classification and retrieval
faults remain explicit. `SourceType` 1/3 maps to calculated/formula, 0/2/null
does not; prompt type 4 is not classified by this rule set.

## Safety and reporting

DTDs and external entities are prohibited. Each XML input is limited to
1,048,576 characters; FetchXML is additionally bounded to 10,000 elements and
64 ancestor levels. This is not a complete FetchXML XSD validator.

The XML editors indent and syntax-colour local copies without changing
collected definitions. Formatting preserves values, mixed content and
`xml:space`, prohibits DTDs, and bounds input, depth and output. Invalid input
is retained with an explanation. Manual-analysis hashes and line numbers refer
to the current editor contents; selected-view analysis retains the original XML.

Exact telemetry markers are not ownership evidence. Shared/system/deleted or
inaccessible markers remain unresolved; they never trigger fallback to all
views. Actor-only candidates offer views for explicit review. A supplied table
name narrows the candidate definitions; a missing table explicitly means no
table narrowing. Original Entra/Dataverse actor identity is retained separately
from the resolved collection user.

No credentials are stored by this tool. The XrmToolBox host manages connection
authentication. Reports can contain user/view IDs, table names, metadata,
existing hint values and service error messages. JSON omits raw FetchXML/layout;
reports are still potentially sensitive and require review before sharing.
CSV export quotes fields and neutralizes common spreadsheet formula prefixes.

## References

- [SDK impersonation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/impersonate-another-user)
- [Query anti-patterns](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/query-antipatterns)
- [SourceType values](https://learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.metadata.attributemetadata.sourcetype)
- [XrmToolBox base control](https://www.xrmtoolbox.com/documentation/for-developers/plugincontrolbase-base-class/)
