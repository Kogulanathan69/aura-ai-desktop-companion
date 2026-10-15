# Step 14J — production audit-delivery composition readiness

## Purpose and verdict

**AuditDelivery: false. CompositionComplete: false — NOT READY.** This step tightens an observational composition rule. It does not register a dispatcher, start delivery, add a worker or timer, apply a migration, or enable execution. Normal `Aura.Api/Program.cs` still uses `UnavailableRuntimeCapabilityReadiness` and registers no `IAuditOutboxDispatcher`.

## Existing outbox and dispatcher foundation

`SecurityAuditOutbox` holds a bounded, durable intent with a stable `AuditEventId`, exact event/action/user/conversation/project/tool/approval/execution bindings, `DeliveryState`, `AttemptCount` and `DeliveredAt`. The execution-state store writes intent in the same transaction as a successful state transition. The entity and targeted migrations provide shape constraints, including Step 13J's Failed row rule. Source and migration presence do not prove that a target database has applied them or that delivery has occurred.

`SecurityAuditOutboxDispatcher` implements `IAuditOutboxDispatcher` as an **explicit one-item operation** keyed by a nonempty outbox ID. It opens a transaction, conditionally locks one Pending row with zero attempts and null `DeliveredAt`, rereads it, validates a bounded restored event and exact execution-state binding, then calls the injected `IAuditEventSink` once. There is no table scan, automatic startup dispatch, hosted service, scheduler, polling or retry loop.

An accepted sink result commits `Delivered`, `AttemptCount=1` and a UTC `DeliveredAt`. A bounded Failed result commits `Failed`, one attempt and null `DeliveredAt`; a terminal row cannot be dispatched again. An Unavailable sink result returns without commit, leaving Pending/zero attempts/null timestamp for a separately chosen future action. Cancellation propagates and may roll back a transaction. Row-lock and conditional updates bound concurrent explicit attempts. The direct database sink accepts an exact duplicate event ID only when every typed field matches; it rejects a mismatched duplicate. This is database idempotency for that sink, **not** exactly-once external delivery. A crash or cancellation after an external sink side effect can leave Pending and permit a later explicit attempt. Production recovery and external idempotency remain unreviewed.

`SecurityAuditEvents` and `IAuditEventSink` describe persistence of one accepted audit event. AuditSink readiness cannot imply AuditDelivery. `ILogger`, generic `AuditLogs`, a manual `DispatchAsync` method, outbox tables or disposable live-harness results cannot imply delivery readiness either. The separate Step 13H–13K checks cover detailed dispatcher transitions and schema hardening; this step does not repeat a live PostgreSQL run.

## Fail-closed composition rule and changes

The inspector now requires exactly one scoped `IAuditOutboxDispatcher` descriptor whose concrete implementation is exactly `SecurityAuditOutboxDispatcher` **and** whose type is in the independently reviewed-production catalog. The catalog remains empty. Factory, instance, duplicate, singleton, transient or unreviewed registrations cannot pass. A catalog entry without matching DI and matching DI without review remain insufficient. The inspector only reads descriptors; it has no DB, network, file, provider or tool side effects.

Step 14J adds offline checks for these registration shapes, the durable outbox and exact-bound dispatcher structure, state/attempt/timestamp transitions, independence from sink/logging/flags, and unchanged normal API startup. No production audit delivery or execution service is registered.

| Composition field | Current value |
| --- | --- |
| Ownership | false |
| Permission | false |
| ExecutionState | false |
| AuditSink | false |
| AuditDelivery | false |
| Reconciliation | false |
| Verification | false |
| ExecutionPolicy | false |
| ToolHandlers | false |
| TrustedExecution | false |
| **CompositionComplete** | **false — NOT READY** |

`ProductionAdaptersEnabled`, `ToolExecutionEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` remain default false. Background delivery, tool execution, Safe File Access and Cloud AI remain disabled.

## Remaining blockers and validation

Before AuditDelivery can be reviewed as production ready, independently verify the migration baseline and Step 13J constraint on the intended database; review live concurrent dispatch, rollback and cleanup evidence; define manual or automated recovery policy, external sink idempotency and crash-window behavior; and separately review a scoped normal-startup dispatcher registration and its sink/context lifetimes. Only a later reviewed change may add the type to the catalog. All other Step 13L capabilities and final security approval remain independent blockers.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
