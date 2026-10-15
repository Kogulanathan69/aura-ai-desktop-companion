# Step 14M — production execution-policy composition readiness

## Purpose and scope

**ExecutionPolicy: false. CompositionComplete: false — NOT READY.** This step tightens an observational registration check. It does not add an allowing policy, register execution services or handlers, start a worker, apply migrations, or enable production execution. Normal `Aura.Api/Program.cs` continues to use `UnavailableRuntimeCapabilityReadiness`.

## Current policy and execution path

`IToolExecutionPolicy` is an independent exact-tool allow decision. The only implementation in production assemblies is `DenyToolExecutionPolicy`; it returns false and propagates cancellation. There is no `DefaultDenyExecutionPolicy` class or allowing production policy. Normal API startup registers no `IToolExecutionPolicy`, so no production lifetime is composed. Allowing policies in offline security fixtures are synthetic test inputs, not production implementations or approval evidence.

`TrustedToolExecutionService` requires an approved action and bound approval, validates ownership, resolves the exact tool and handler, and checks exact permission. It then calls `policy.AllowsAsync` before reserving durable state or invoking a handler. A false policy result returns `PolicyDenied`; unknown tools or invalid action states also deny. Ownership, permission and approval are necessary gates, but none supplies the independent policy allow decision. Handler or trusted-service presence cannot bypass it. The `DisabledToolDispatcher` remains disabled. The execution service has no fallback allowing policy and propagates cancellation.

`RuntimeSafetyOptions` defaults `ProductionAdaptersEnabled`, `ToolExecutionEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` to false. Flags alone do not register or review a policy. Step 13L still requires a separately reviewed allowing policy, real handlers, other capability evidence and final security approval.

## Fail-closed composition rule and changes

The inspector now requires exactly one scoped `IToolExecutionPolicy` service descriptor with a concrete type in the source-controlled reviewed production policy catalog. `DenyToolExecutionPolicy` is explicitly excluded, even if registered. The catalog remains empty because no eligible allowing production policy exists. A factory, instance, duplicate, singleton, transient, unavailable or unreviewed implementation cannot pass. A future exact type may enter the catalog only after its intentional normal-startup registration, lifetime, decision semantics, bypass resistance and fail-closed behavior are reviewed. Catalog membership without matching DI is insufficient, as is DI without review.

Step 14M changes only the inspector check, adds focused offline checks and this document. The inspector reads descriptors only and does not resolve services or access a database, network, filesystem, provider or tool. No worker, poller, retry loop, timer, scheduler or background execution is added.

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

## Remaining blockers and validation

Before ExecutionPolicy may be reviewed as ready, design and independently assess an exact-tool production allowing policy; verify its fail-closed decisions, cancellation and bypass resistance; intentionally compose its scoped DI registration; and then explicitly review its catalog entry. Ownership, grant lifecycle, approval, durable state, audit, reconciliation, independent verification, real handlers, trusted execution and Step 13L final approval remain separate gates. Disposable database evidence and configuration flags cannot clear this policy gate.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
