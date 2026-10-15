# Step 14H — production execution-state composition readiness

## Purpose and current verdict

**ExecutionState: false. CompositionComplete: false — NOT READY.** This step tightens the observational Step 14E composition boundary for durable execution state. It does not activate execution, register a production store, apply a migration, or change feature flags. Normal `Aura.Api/Program.cs` has no `IActionExecutionStateStore` registration and still supplies `UnavailableRuntimeCapabilityReadiness` to runtime safety validation.

## Existing durable-state foundation reviewed

`ActionExecutionStatePersistenceStore` implements `IActionExecutionStateStore` using PostgreSQL through EF Core and a fresh `AuraDbContext` from a context factory for each operation. The application state and persisted row bind exact `ActionId`, internal `UserId`, `ConversationId`, `ProjectId`, `ToolId`, `ApprovalId`, `Version` and `ExecutionId`. The public execution request does not supply an execution ID; the trusted workflow creates one attempt ID. The store neither invokes a handler nor performs an external tool effect.

Initial reserve uses a transaction to insert a Ready/version 0 row if absent and conditionally change it to Reserved/version 1. Existing rows must match the exact binding and state. Conditional `ExecuteUpdateAsync` predicates compare action, user, conversation, project, tool, approval, version, state, execution ID and approval-consumption shape. Completion also requires the same reserved execution ID and expected version 1 before writing Succeeded or Failed/version 2. One affected row is required. Stale versions, duplicate reservation/completion and terminal reopening fail closed. Cancellation propagates, and the store has no retry/fallback loop.

The `ActionExecutionStates` primary key binds one row per action; a unique approval index prevents cross-row approval reuse, and a filtered unique execution index prevents non-null execution ID reuse. Check constraints pin nonempty IDs and Ready/Reserved/terminal shapes. These schema rules and a targeted migration exist in source; they are not proof of a production database's applied migration or current integrity. Individual foreign keys do not themselves prove the complete action/approval/user/project relationship. The existing Step 11S/11T/13C/13D checks cover the detailed state policy, replay and concurrency behavior. The user reports a controlled live Step 13D PASS, but that result alone does not constitute a reviewed production DI registration or close the historical migration-baseline and wider activation gates.

## Fail-closed composition rule and changes

The inspector now requires exactly one scoped `IActionExecutionStateStore` descriptor whose concrete implementation type is exactly `ActionExecutionStatePersistenceStore` **and** is independently listed in the reviewed-production catalog. The catalog remains empty. An implementation file, interface, entity, table, migration, live harness PASS, factory, instance, duplicate, singleton, transient or unavailable-store registration cannot raise ExecutionState. A catalog entry without matching DI and matching DI without review both remain insufficient.

Step 14H adds focused offline composition checks and wires them into `security-checks/Program.cs`. They cover registration forms, exact source/schema guard presence, independence from ownership, permission, approval and flags, unchanged API registration and default-disabled runtime, and absence of new live or background behavior. Existing semantic and live-harness checks remain in the full suite.

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

Production tool execution, audit persistence, Safe File Access and Cloud AI remain disabled. No trusted execution service, allowing execution policy, handler, audit dispatcher worker, retry loop or automatic migration is added.

## Remaining blockers and validation

Before ExecutionState could be reviewed for production readiness, confirm the exact production schema and migration baseline, assess composite binding integrity and approval lifecycle, review live PostgreSQL evidence for the intended target, and separately review a scoped normal-startup store registration with its context-factory dependency. Only then could a distinct review add the exact implementation to the catalog. Ownership, permission, audit, reconciliation, verification, policy, handlers, trusted composition and the Step 13L final security gate remain independent blockers. A durable reservation does not guarantee exactly-once external effects.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
