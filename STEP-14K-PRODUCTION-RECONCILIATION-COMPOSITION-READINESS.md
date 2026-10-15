# Step 14K — production reconciliation composition readiness

## Purpose and scope

**Reconciliation: false. CompositionComplete: false — NOT READY.** This step tightens an observational registration check. It does not register a reconciliation source, run a worker, mutate execution state, apply migrations, or enable production execution. Normal `Aura.Api/Program.cs` still uses `UnavailableRuntimeCapabilityReadiness`.

## Existing foundation and current truth

`ExecutionReconciliationPersistenceSource` is the only durable `IExecutionReconciliationSource` implementation in source. It is **not registered in normal API startup**, so its production lifetime and dependencies are not composed or reviewed. The separate `UnavailableExecutionReconciliationSource` returns no evidence. No hosted reconciliation worker, timer, polling loop, retry loop, or automatic startup reconciliation is registered.

The persistence source uses a fresh EF context and a RepeatableRead transaction to read `ActionExecutionStates` without tracking. It queries by exact `ActionId`, `UserId`, `ConversationId` and `ProjectId`, then checks the active user, owned project, project conversation, action and approval chain. It validates the canonical tool ID, consumed approval, execution ID, version, status and UTC timestamps before returning a bounded snapshot. The snapshot preserves the exact action, scope, tool, approval, execution and version bindings. Storage cannot infer a cause, so it reports `Reason.Unknown`. The source is read-only and does not use legacy `ToolExecution.Status` as proof.

Missing, foreign, malformed or database-error evidence returns null; cancellation propagates. `ExecutionReconciliationService` validates scope before and after assessment, rejects conflicting bindings and already-resolved states, and issues only advisory evidence for a valid Reserved state. It does not transition execution state. A successful execution outcome is therefore not a successful reconciliation assessment, and stale or inconsistent evidence is not accepted as one.

`ExecutionReconciliationAssessments` and the Step 13E migration define a historical advisory record with exact IDs, Reserved/version-one observation, bounded reason and disposition, and a timestamp. No assessment write adapter is registered. Individual foreign keys do not enforce every composite binding. The table and migration in source do not prove deployment, live behavior, or production readiness. Separate Step 13E/13F checks cover the underlying persistence model; this step adds focused composition checks.

## Fail-closed registration rule

The inspector now requires exactly one scoped `IExecutionReconciliationSource` descriptor with concrete implementation type exactly `ExecutionReconciliationPersistenceSource` **and** that exact type in the independently reviewed production catalog. The catalog remains empty. A factory, instance, duplicate, singleton, transient, unavailable or synthetic source cannot pass. A catalog entry without matching DI and matching DI without reviewed evidence cannot pass. The inspector reads only descriptors and does not resolve services or access a database, file, network, provider or tool.

ExecutionState, AuditSink and AuditDelivery are independent fields; none substitutes for Reconciliation. Flags and disposable live database evidence do not substitute for the reviewed production registration. Step 14K changes only the inspector check, adds offline checks and this document. It does not enable background reconciliation, tool execution, Safe File Access or Cloud AI.

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

`ProductionAdaptersEnabled`, `ToolExecutionEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` retain their false defaults.

## Remaining blockers and validation

Production readiness needs an independently reviewed migration baseline and live PostgreSQL evidence; review of the intended source's context-factory DI and scoped lifetime; stronger composite binding integrity or a documented equivalent; a persistence/idempotency policy for advisory assessments; stale-state and outcome handling under concurrency; and a deliberate normal-startup registration followed by explicit catalog review. Ownership, permission, audit, verification, policy, handlers, trusted composition and final Step 13L security approval remain separate blockers. A disposable live harness PASS alone cannot clear this gate.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
