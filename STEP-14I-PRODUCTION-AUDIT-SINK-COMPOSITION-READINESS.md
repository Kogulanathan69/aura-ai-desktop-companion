# Step 14I — production audit-sink composition readiness

## Purpose and verdict

**AuditSink: false. CompositionComplete: false — NOT READY.** This step tightens an observational composition rule. It does not register an audit sink, enable audit delivery or execution, apply a migration, or change any runtime flag. Normal `Aura.Api/Program.cs` has no production `IAuditEventSink` registration and continues to use `UnavailableRuntimeCapabilityReadiness`.

## Existing audit persistence foundation

`SecurityAuditPersistenceSink` is a direct, unregistered `IAuditEventSink` implementation backed by EF Core. It validates one bounded typed `AuditEventRecord`, maps its event/outcome, UTC time, exact user/conversation/project and optional action/tool/approval/execution/verification/reconciliation/permission-grant identifiers to `SecurityAuditEvents`, and returns Accepted only after one saved row. It maps errors to bounded Failed or Unavailable and propagates cancellation. A duplicate event ID is accepted only when every stored typed field matches the requested event, including its PostgreSQL-microsecond UTC timestamp. A mismatched duplicate fails. It neither retries nor falls back to generic logging.

`SecurityAuditEvents` has its own entity, EF constraints and targeted migration. Legacy `AuditLogs` and `ILogger` are not typed security-audit persistence: neither establishes exact event identity, scope and provenance or an accepted durable write. Entity/migration existence does not establish the deployed database schema, immutability, append-only enforcement or tamper evidence. The direct sink does **not** share a transaction with the execution-state update; observational audit failure can follow a committed business change. It creates no outbox row.

`SecurityAuditOutbox` is a separate bounded intent written by the execution-state store in the same transaction as a successful state transition. It is not delivery proof. `SecurityAuditOutboxDispatcher` is a separate, unregistered `IAuditOutboxDispatcher`; no worker, scheduler or automatic retry exists. Dispatcher presence or future AuditDelivery readiness cannot substitute for AuditSink. The outbox and direct sink together still require reviewed PostgreSQL behavior and an external-effect idempotency/recovery decision; exactly-once delivery is not claimed.

## Fail-closed composition rule and changes

The inspector now requires exactly one scoped `IAuditEventSink` descriptor whose concrete implementation is exactly `SecurityAuditPersistenceSink` **and** whose type is independently in the reviewed-production catalog. That catalog remains empty. Factory, instance, duplicate, singleton, transient, unavailable or generic-log registrations cannot pass. A catalog entry without matching DI and matching DI without review both remain insufficient. The inspector reads descriptors only and has no DB, network, file, provider or tool side effects.

Step 14I adds focused offline checks for those registration shapes, exact audit model and duplicate binding, failure/cancellation visibility, independence from outbox delivery, other capabilities and flags, and unchanged normal API startup. Existing Step 13E–13H and later audit/outbox checks continue to cover detailed semantics; this step does not rerun live PostgreSQL verification.

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

`ProductionAdaptersEnabled`, `ToolExecutionEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` remain default false. Production audit persistence, outbox delivery, tool execution, Safe File Access and Cloud AI remain disabled.

## Remaining blockers and validation

Before AuditSink could be reviewed as production ready, independently verify the controlled PostgreSQL schema and migration baseline, event binding and durability behavior, transaction/outbox coupling and failure windows, append-only/retention and tamper policy where required, duplicate handling and delivery recovery, and a deliberate scoped normal-startup registration. Only a separate reviewed change may add the exact type to the catalog. All other Step 13L capabilities and final approval remain independent blockers.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
