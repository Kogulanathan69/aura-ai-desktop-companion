# Step 11V — Permission enforcement foundation

## Scope and architecture

Step 11V adds a bounded Application-layer permission source, exact-match policy, validation result, and `IToolExecutionPermissionValidator` implementation. It keeps the coordinator interface and gate order unchanged: `TrustedToolExecutionService` still validates approval and ownership, resolves the exact tool, checks permission before execution policy and durable reservation, rechecks ownership, then reserves before any handler call. There is no production permission repository, DB adapter, or allow registration. The existing deny validator and new unavailable source preserve default denial.

`ToolDescriptor.RequiredPermission` is one bounded `ToolPermissionRequirement`: `OwnedProjectRead`, `OwnedProjectWrite`, or `ExternalAction`. It declares a requirement; it is not a grant, approval, ownership proof, execution-policy decision, or authorization merely because a tool is registered. Unknown requirement values fail closed. No role hierarchy, wildcard, user-global, project-wide, or cross-conversation/tool grant is modeled.

## Request, source, policy, and result

`ToolPermissionValidationRequest` contains only server-known `ActionScope` (UserId, ConversationId, ProjectId), resolved ToolId, and resolved descriptor requirement. It has no role name, claim, approval flag, arbitrary permission string, path, command, URL, or payload. The public chat request remains Prompt-only and cannot submit a grant or requirement.

`IToolPermissionSource.GetAsync` receives that exact request and returns one bounded lookup result: `Found` with a `ToolPermissionGrant`, `NotFound`, or `Unavailable`. The grant contains only GrantId, exact scope, ToolId, requirement, `IsActive`, and `IsRevoked`. It is source data, not a reusable execution capability. The supplied `UnavailableToolPermissionSource` returns Unavailable without DB or OS access. No grant or `Permission` domain entity is created or written automatically.

`ExactToolPermissionPolicy` allows only a Found grant with a nonempty GrantId, exact user/conversation/project/tool/requirement match, active state, and no revocation. Missing, unavailable, inactive, revoked, malformed, mismatched, or unknown enum data denies. Bounded decisions are `Allowed`, `Denied`, `Unavailable`, `Revoked`, `ScopeMismatch`, `RequirementMismatch`, `InvalidGrant`, and `Failed`. Fixed result messages are at most 80 characters and contain no raw exception, source text, JWT, role, path, stack, or secret. The public denial factory cannot fabricate Allowed.

`ToolExecutionPermissionValidator` constructs the request from the server-owned scope and resolved descriptor, calls the source **on every validation**, then calls the exact policy. It has no cache, retry, fallback, or permission creation. Cancellation propagates before and after each async boundary. Non-cancellation source or policy exceptions map to fixed `Failed` and boolean denial. The existing coordinator calls `HasPermissionAsync` once per execution attempt before reservation; it receives only a boolean, so any decision other than Allowed prevents handler invocation and state reservation.

Permission is independent of ownership, immutable approval evidence, execution policy, and durable reservation. A grant cannot bypass those gates, and none of those gates creates permission. A previously allowed grant revoked before a later call is read afresh and denied. This foundation does not provide production authorization, JWT/role mapping, PostgreSQL Permission-row enforcement, or atomicity between a permission check and later reservation.

## Limits and verification

Data is limited to fixed GUIDs, the existing bounded ToolIdentifier, three-value requirement and lookup enums, the bounded decision enum, and two booleans. There are no arbitrary strings, metadata dictionaries, or collections. Package-free checks cover exact grants and scope/requirement binding, missing/unavailable/revoked/inactive grants, malformed IDs and enums, fresh revocation, cancellation, safe dependency failures, bounded messages, and coordinator denial before reservation/handler. Synthetic checks use only a fake source and handler; no production allow source is registered.

No DbContext, EF configuration, schema, migration, package, real permission adapter, real handler, shell/Git/filesystem/browser/HTTP/MCP execution, automatic verification, chat/provider integration, or real PostgreSQL/OpenAI/Ollama call is added. Safe File Access remains disabled. Production ownership and permission source behavior, PostgreSQL/pgvector, real tool execution, semantic retrieval/ranking, and live OpenAI/Ollama remain runtime-unverified and deferred. This is not production execution readiness.
