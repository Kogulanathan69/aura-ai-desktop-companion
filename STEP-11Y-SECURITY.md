# Step 11Y — Production adapter readiness and authorization boundaries

Step 11Y adds Application contracts and a shared ownership decision for future production adapters. It does not add a production adapter, persistence, or an executable tool path.

## Ownership and authenticated user

`ExactOwnershipScopeValidator` implements the action, approval, execution, verification, and reconciliation scope-validator interfaces. Every call obtains a fresh `CurrentUserSnapshot` from `ICurrentUserContext`, then requests one exact `OwnershipLookupRequest` from `IOwnershipSource`. The authenticated user must be nonempty and equal the scope user. The source result must be `Found`, echo the exact user/conversation/project binding, and independently affirm owned conversation, exact conversation-to-project relation, and owned project. A supplied ID or returned ID alone never proves ownership. Empty, foreign, mismatched, partial, unavailable, and unexpected results deny. Cancellation propagates; other adapter errors become denial without leaking details. There is no cache, retry, fallback, or wildcard.

The current-user contract exposes only a Guid and availability status. A future host adapter must derive them from its authenticated request, not from `ActionScope`; it must not pass JWTs, claims, tokens, roles, or email into Application. The ownership-source contract accepts only three IDs and returns bounded status, binding, and ownership facts. A future Infrastructure adapter must check current trusted data for the exact relation and owners without returning entity graphs or query text. `UnavailableCurrentUserContext` and `UnavailableOwnershipSource` deny by default. No production implementation of either exists.

## Other adapter contracts

- `IToolPermissionSource` must use the exact scope, resolved tool, and requirement against current active, non-revoked permission state. No wildcard, role inference, cache, or automatic grant. `ExactToolPermissionPolicy` still rejects mismatched and revoked grants; permission does not establish ownership.
- `IActionExecutionStateStore` requires atomic initial creation/reservation, compare-and-swap version checks, single approval consumption, exact action/scope/tool/approval/execution binding, and consistent terminal completion. It must never invoke a handler or perform tool side effects. The unavailable store remains the default contract implementation.
- `IAuditEventSink` accepts one bounded `AuditEventRecord` at a time. `Accepted` means the sink accepted responsibility, not that durable persistence is proven. Cancellation may follow acceptance; Application does not retry. Audit presence grants no authorization. The unavailable sink remains available for fail-closed wiring.
- `IExecutionReconciliationSource` must return current bounded state with exact action, scope, tool, approval, execution, and version bindings. No raw output, logs, exceptions, or automatic recovery. Its unavailable implementation returns no snapshot.
- `IExecutionEvidenceSource` must return trusted bounded evidence for exact scope and execution identity. It cannot return raw handler output or manufacture `Verified`; its unavailable implementation returns no evidence. Verification cannot grant ownership or permission.

Ownership, permission, approval, execution policy, durable reservation, verification, and audit remain separate checks. Passing any one check does not imply another. Audit and verification never authorize execution; permission or ownership alone cannot execute a tool.

## Runtime and remaining work

`Aura.Api/Program.cs` has no Step 11Y workflow adapter registration or production-enablement option. The new contracts therefore do not make an allow-capable workflow reachable at startup. Safe File Access remains disabled by default. No real ownership, permission, state-store, audit, reconciliation, or verification adapter was added. No DbContext, EF configuration, schema, migration, package, real handler, or chat/provider behavior was changed. This step makes no PostgreSQL, OpenAI, or Ollama runtime call.

Future work must supply authenticated request mapping and trusted, transactional production adapters, wire them deliberately, then validate ownership and all other boundaries against the live runtime. PostgreSQL/pgvector integration, production ownership validation, semantic retrieval/ranking, live OpenAI/Ollama runtime, and AiChatService integration remain unverified or deferred. This is not production readiness.

The package-free Step 11Y security checks exercise the shared validator's exact binding, all five entry points, default denial, partial/foreign results, safe failure, and cancellation. Prior checks continue to cover permission, reservation, audit, verification, disabled tools, and Safe File Access. With .NET 10.0.401, `dotnet build Aura.slnx --no-restore` passed with no warnings or errors, and the full security suite passed 1,066 checks. The security-check project emitted two existing nullable warnings during its own build.
