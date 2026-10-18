# Step 15J — trusted execution production evidence and registration review

## Purpose and scope

**TrustedExecution: false. CompositionComplete: false — NOT READY.** This review pins the existing application orchestration and production registration boundary offline. It adds no executable production route, handler, allowing policy, service registration, worker, migration or runtime enablement.

## Contract, implementation and dependencies

`ITrustedToolExecutionService.ExecuteAsync` accepts `ActionScope`, `ActionDescriptor`, `ApprovalEvidence`, `ExecutionRequest` and cancellation token, returning `ExecutionOperationResult`. `ExecutionRequest` contains only ActionId; it cannot carry a command, path, URL, version, execution ID or bypass flag. The result exposes a bounded fixed message plus optional action/evidence; it does not expose a raw handler exception or arbitrary handler output. The sole concrete production implementation is sealed `TrustedToolExecutionService`.

Its constructor takes `ToolRegistry`, `IToolExecutionScopeValidator`, `IToolExecutionPermissionValidator`, `IToolExecutionPolicy`, `IDateTimeProvider`, `IActionExecutionStateStore` and optional `IAuditEventWriter`. The optional writer falls back to `UnavailableAuditEventWriter`. Ownership ultimately depends on authenticated identity and exact owned scope; permission needs an exact grant source; the store is the durable reservation/completion boundary. Audit sink/outbox delivery, reconciliation and independent verification are separate capabilities, not executor dependencies. The current production API does not register this service or a complete dependency graph.

## Actual gate and side-effect order

1. Check cancellation and structural request, action/scope match, approved action state and bound approval evidence.
2. Validate ownership of the exact user/conversation/project scope; resolve the exact registered tool and handler.
3. Check permission for that scope and descriptor; check the independent execution policy for that descriptor.
4. Revalidate ownership; validate UTC time; create a bound Ready state and server execution attempt.
5. Call `TryReserveInitialAsync`; require successful, exact Reserved action/scope/tool/approval/execution/version-1 state.
6. Record an observational `ExecutionReserved` audit event; mint trusted context and begin the action transition.
7. **Side effect can begin only at the single `handler.ExecuteAsync(new ToolInvocationRequest(action.ToolId), cancellationToken)` call.**
8. Map the handler result to Succeeded/Failed; call exact versioned `TryCompleteAsync`; require matching terminal state and version increment.
9. Issue execution evidence and completed action; record observational `ExecutionCompleted` or `ExecutionFailed` audit event.

Approval is checked **before** the first ownership call in the actual code, unlike the conceptual sketch that placed it later. Ownership is checked before permission, and permission before policy. The handler cannot be invoked without a successful reservation and exact state validation. An unavailable, conflicting or malformed reservation prevents the call; uncertain reservation is reported as `ReconciliationRequired`. The optional audit observation is not a durable audit-success gate: unavailable/throwing audit writers do not change the business result or retry a side effect. Cancellation during the pre-handler audit observation propagates and leaves Reserved state with no handler call.

The unregistered `ActionExecutionStatePersistenceStore` stages a bounded Pending audit-outbox intent in the **same transaction** as a successful reservation and commits before reporting success. That would leave a durable reservation and pending intent before a side effect when that store is deliberately composed. Its live PostgreSQL behavior and historical migration baseline remain separately unproven here. The observational `IAuditEventWriter` event may fail independently and is not the outbox delivery signal. Post-handler audit observation does not retry the handler.

## Failure, cancellation and crash windows

Handler failure or exception maps to fixed Failed status and one attempted durable completion. Cancellation propagates, including during handler or completion, without fabricating a completed action. If the handler has run and completion fails, is uncertain, has invalid binding or cannot be persisted, the service returns bounded `ReconciliationRequired` and does not call the handler again. A crash after the side effect and before completion can leave Reserved state and pending audit intent. Reconciliation must assess that state; it cannot infer whether an external side effect occurred merely from a reservation. **Exactly-once external effects are not guaranteed.** There is no retry, alternate handler/policy, polling, scheduler, provider fallback, file/process fallback, automatic migration or schema creation in this service. A caller could invoke a future registered service directly; production activation must ensure all dependencies and entry points are reviewed, rather than relying on an endpoint alone.

`AuditObservation` swallows non-cancellation writer failures and performs no retry. This prevents audit failure from re-running the handler, but the optional unavailable-writer fallback is a production audit-readiness blocker. AuditSink and AuditDelivery must be independently ready and a future DI review must establish the intended writer/outbox graph before any production registration.

## Reconciliation and verification separation

Successful handler status and durable completion produce an **execution report**, not independent verification. The evidence's `ToVerificationInput` is only a structural handoff. `TrustedToolExecutionService` does not mark an action Verified. `ExecutionReconciliationService` assesses reserved durable state and emits advisory evidence without silently completing or verifying it. `VerificationWorkflowService` requires a separate evidence source and policy. Neither reconciliation nor handler success may fabricate Verified status.

## Production DI and nine prerequisites

Normal `Aura.Api/Program.cs` registers no `ITrustedToolExecutionService`, and the source-controlled `ReviewedTrustedExecution` catalog is empty. `ProductionRuntimeCompositionInspector` requires exactly one scoped descriptor for the exact `TrustedToolExecutionService` type, that type in the reviewed catalog, **and all nine independent fields**: Ownership, Permission, ExecutionState, AuditSink, AuditDelivery, Reconciliation, Verification, ExecutionPolicy and ToolHandlers. Missing, duplicate, wrong-lifetime, factory, instance or unreviewed registration fails. Any one false prerequisite keeps TrustedExecution false. Flags cannot populate the catalog or overcome the gate.

Current full snapshot: Ownership=false, Permission=false, ExecutionState=false, AuditSink=false, AuditDelivery=false, Reconciliation=false, Verification=false, ExecutionPolicy=false, ToolHandlers=false, TrustedExecution=false; **CompositionComplete=false — NOT READY**. Step 14P's final activation review and approval remain separate. Steps 15C–15I record the independent evidence and registration blockers.

**LIVE DATABASE EVIDENCE: NOT APPLICABLE** to this offline orchestration review because the trusted service is not registered and its production prerequisites are unavailable. No live command was run. Live durable-state, audit, reconciliation and verification evidence remain separate requirements; an offline service scenario is not a production PASS claim.

## Remaining blockers and validation

Review the real-token ownership and grant lifecycle, historical migration baseline and live PostgreSQL state/outbox behavior, durable audit sink/delivery, reconciliation and independent verification, genuine allowing policy, exact real handlers, registry binding, explicit writer/store DI graph and lifecycle, direct-call entry points, and Step 14P final security approval and executable activation gate. Only then could a catalog entry and normal-startup registration be considered.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
