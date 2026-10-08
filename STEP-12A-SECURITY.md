# Step 12A — Production persistence adapter foundation

This step assesses existing EF/domain shapes against the Step 11Y contracts and adds only one read-only Infrastructure adapter. It does not enable production authorization or tool execution.

## Implemented ownership reader

`OwnershipPersistenceSource : IOwnershipSource` uses the existing `IAuraDbContext` sets. One bounded, no-tracking `AnyAsync` query requires an active `User.Id` equal to the requested internal user ID, a `Project` owned by that ID, and a `Project`-type `Conversation` with the exact requested ID, owner, and `ProjectId`. The returned `OwnershipLookupResult` contains only status, the exact request binding on success, and three true facts; missing, foreign, inactive, orphaned, and General conversations return the same denied result. Invalid IDs deny before query. Query failures return `Unavailable` without exception details, retry, fallback, or writes; cancellation propagates. There is no raw SQL, dynamic predicate, entity graph, or wildcard.

The reader is not an authenticated-user context. `ICurrentUserContext` remains unavailable in production, and its trusted mapping from an authenticated request is deferred. The database mapping alone cannot authorize a workflow. EF query translation and PostgreSQL behavior have not been verified against a live database.

## Deferred adapters and schema gaps

| Contract | Existing shape and reason for deferral |
| --- | --- |
| `IToolPermissionSource` | `Permission` has user and optional project, generic resource/access strings, status, and revocation time. It has no exact conversation, typed tool ID, or typed permission requirement binding. No role/resource-string inference is safe. |
| `IActionExecutionStateStore` | `AIAction` and `ToolExecution` do not represent the required version/CAS state, exact attempt ID, exact scope/tool/approval binding, unique approval consumption, and transactional reserve/complete semantics. No state store was force-fit. |
| `IAuditEventSink` | `AuditLog` has no exact conversation and bounded reference set. Its description and old/new values are free text/JSON fields; IDs were not serialized or hidden in them. No durable sink exists. |
| `IExecutionReconciliationSource` | Exact persistent execution state and version do not exist, so a trusted bounded snapshot cannot be reconstructed. |
| `IExecutionEvidenceSource` | `ToolExecution`, `AIAction`, `ActionApproval`, and `Verification` do not establish all exact execution/action/scope/tool/approval/completion/outcome provenance required by the contract. Generic status and optional links cannot be treated as trusted evidence. |

No permission, action, approval, execution, verification, or audit row is automatically written. No new schema, migration, DbSet, EF configuration, package, cache, outbox, or persistence workaround was added.

## Readiness and runtime

The bounded `IRuntimeCapabilityReadiness` contract moved from API to Application so Infrastructure can implement it without depending on API. `PersistenceCapabilityReadiness` reports only the ownership *reader implementation* as present; all other flags remain false. It is informational and is not registered. `Aura.Api/Program.cs` still constructs `UnavailableRuntimeCapabilityReadiness`, reports all capabilities unavailable, and registers no new persistence adapter. Runtime safety defaults, Safe File Access disabled state, and no real tool handler remain unchanged. No JWT/raw-claims integration or live OpenAI/Ollama call was added.

Package-free checks use the repository's existing query-only `DbSet` test seam to cover exact ownership, foreign/missing/empty IDs, project relation, inactive user, read-only behavior, cancellation, safe error mapping, and no retry. They do not verify EF SQL translation, transactionality, or live PostgreSQL. With .NET 10.0.401, `dotnet build Aura.slnx --no-restore` passed and the full suite passed 1,094 checks. Production adapters and runtime security remain unverified and are not ready for enablement.
