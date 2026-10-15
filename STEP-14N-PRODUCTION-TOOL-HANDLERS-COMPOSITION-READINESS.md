# Step 14N — production tool-handlers composition readiness

## Purpose and scope

**ToolHandlers: false. CompositionComplete: false — NOT READY.** This step tightens an observational registration check. It does not register handlers, invoke a tool, enable Safe File Access, start a worker, apply migrations or enable production execution. Normal `Aura.Api/Program.cs` retains `UnavailableRuntimeCapabilityReadiness`.

## Current handler and registry truth

There are **no concrete `IToolHandler` implementations in the production Application or Infrastructure assemblies** and no `IToolHandler` registration in normal API startup. There are therefore no production handler tool IDs, read-only handlers, state or file mutation handlers, process or shell handlers, or Safe File Access handlers to classify. Handler classes in offline security checks are test fixtures; they are not registered in the API and do not establish production readiness. No automatic handler discovery or assembly scanning exists in normal startup.

`ToolIdentifier` enforces a bounded canonical ID. `ToolDescriptor` carries an explicit capability (`ProjectRead`, `ProjectWrite`, `ExternalAction`) and matching permission requirement (`OwnedProjectRead`, `OwnedProjectWrite`, `ExternalAction`); its approval and owned-scope requirements are true and `Enabled` remains false. These are metadata, not authorization. `ToolRegistry` rejects duplicate IDs and an unknown ID has no fallback handler. `DisabledToolDispatcher` returns Disabled or UnknownTool without invoking a handler. `TrustedToolExecutionService` separately requires bound approval, ownership, exact permission and independent policy allowance before its sole handler call; cancellation propagates. Existing tool and execution checks cover those behavioral gates in more detail.

## Reviewed exact-set rule and changes

The inspector now compares only explicit `IToolHandler` service descriptors with a source-controlled reviewed catalog of `(handler type, canonical tool ID, capability, permission requirement)`. The catalog remains **empty**. Empty means not ready, regardless of how many source files or registrations exist. A future nonempty catalog must have unique types and tool IDs, a bounded count, canonical IDs and matching capability/permission pairs. Readiness requires the registration count to equal the reviewed count and exactly one scoped concrete registration for each reviewed type. Factory, instance, duplicate, missing, extra, transient, singleton and unreviewed registrations fail. The catalog does not use assembly scanning, runtime reflection discovery, configuration strings or plugins as handler identity. The offline checks exercise hypothetical catalogs against a private pure comparison method; they do not populate the production catalog.

This descriptor check cannot itself instantiate a future `ToolRegistry` or prove that a reviewed handler type is bound to the catalog's tool ID in a running executor. Before any catalog entry is added, the explicit registry construction, tool ID, actual capability and permission mapping, handler side effects and production DI must be reviewed together. In particular, a single handler cannot satisfy a multi-handler reviewed set; an empty catalog cannot be satisfied by one handler. Source existence, policy readiness, trusted-service presence and runtime flags cannot substitute for an exact set.

Safe File Access remains a separate disabled capability. No handler uses it today, and this step adds no file, write, execute, shell, process, network, provider or background capability.

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

`ProductionAdaptersEnabled`, `ToolExecutionEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` retain false defaults.

## Remaining blockers and validation

Production use needs an explicit reviewed handler design and complete set, each handler's exact ID/capability/permission and side-effect classification, safe registry composition, cancellation and failure semantics, and intentional scoped normal-startup registrations. Any future file or process capability needs its own security review. Ownership, permission lifecycle, approval, policy, durable state, audit, reconciliation, independent verification, trusted execution and Step 13L final approval remain separate gates. No source-only or disposable live-database result clears this gate.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
