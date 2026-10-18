# Step 15I — production tool-handlers evidence and registration review

## Purpose and scope

**ToolHandlers: false. CompositionComplete: false — NOT READY.** This offline review inventories handlers, checks the explicit registry and trusted execution boundary, and records what an eventual reviewed production handler set must satisfy. It adds no handler, production DI registration, runtime enablement, migration or live harness.

## Handler inventory and contract

`IToolHandler` exposes `Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken cancellationToken)`. There are **no concrete `IToolHandler` implementations in Aura.Application, Aura.Infrastructure or Aura.Api**. Consequently there is no production side effect, file/OS/process/network operation, command/path/URL parser, secret handling, retry, idempotency mechanism or audit behavior to review per handler. No production handler set exists.

Private test-only implementations appear in `ActionChecks`, `ApprovalChecks`, `AuditWorkflowChecks`, `ExecutionIntegrationChecks`, `ExecutionChecks`, `ProductionExecutionPolicyCompositionChecks`, `ProductionToolHandlersCompositionChecks` (two), `ToolChecks`, `ToolPermissionChecks` and `VerificationChecks`. They are synthetic fixtures in `security-checks`, not API registrations or reviewed production implementations. The Step 15I checks reuse an existing fixture for registry tests and add no new handler.

## Descriptor, IDs and registry

`ToolDescriptor` has immutable ID, display name, description, `ToolCapability` (`ProjectRead`, `ProjectWrite`, `ExternalAction`), `ToolPermissionRequirement` (`OwnedProjectRead`, `OwnedProjectWrite`, `ExternalAction`) and fixed `RequiresApproval=true`, `RequiresOwnedScope=true`, `Enabled=false`. The constructor rejects malformed/beyond-limit text and undefined metadata enum values. Metadata classifies a tool; it does not grant access. `ToolIdentifier.TryCreate` bounds the original input to 1–64 characters, trims and lowercases it, then requires an ASCII letter followed only by ASCII letters, digits or hyphens. Paths, URLs and arbitrary type names do not qualify as canonical IDs.

`ToolRegistry` consumes an explicit `IEnumerable<ToolRegistration>` and snapshots at most 32 unique tool IDs. It rejects null registrations, descriptors/handlers, over-limit sets and duplicate IDs after normalization. It returns no handler for an unknown ID. **The runtime registry does not reject the same handler instance/type used for two distinct IDs.** Step 14N's *production composition* exact-set predicate separately rejects duplicate reviewed handler types and duplicate reviewed IDs. Future registry construction must be compared with the reviewed catalog so the descriptor actually paired with each handler matches its catalog ID/capability/permission; the descriptor-only inspector cannot prove runtime registry binding by itself.

`DisabledToolDispatcher` returns `InvalidRequest`, `UnknownTool` or `Disabled` without calling a handler. It has no fallback. Neither the registry nor normal API startup scans assemblies or loads plugins for handlers. No handler, `ToolRegistry`, or `IToolDispatcher` is registered in normal `Aura.Api/Program.cs`.

## Execution gates and failure behavior

The separate `TrustedToolExecutionService` requires an approved action and bound approval, validates exact ownership scope, resolves the registered tool/handler, checks permission and independent policy allowance, validates scope again, and obtains a durable reservation before its one handler call. Handler presence alone cannot bypass ownership, permission, approval, policy or durable state. Step 15C and Step 15H review permission and policy separately; neither is ready for production. The service is not registered in normal API startup.

The handler request contains only `ToolId`, with no arbitrary command, path, URL, payload or permission-bypass field. `ToolExecutionResult` has a fixed status and bounded fixed message; undefined status maps to `Failed`. A handler exception is mapped to a fixed failure result and does not expose raw exception text. `OperationCanceledException` propagates; cancellation after reservation leaves a state requiring reconciliation rather than fabricating completion. The trusted path has one handler invocation and no retry or fallback handler. Actual future handlers require individual review of side effects, data access, secrets, cancellation, errors, external idempotency, reversibility and audit observability before any production use.

## Production composition and future exact-set requirements

`ReviewedToolHandlers` is an empty source-controlled catalog. The Step 14N inspector requires a **nonempty**, bounded reviewed list of `(exact handler type, canonical ToolId, capability, required permission)`. It requires unique handler types and IDs, approved capability/permission pairings, and exactly one scoped concrete `IToolHandler` descriptor for each reviewed entry. A missing or extra handler, duplicate registration, factory, instance, transient or singleton cannot pass. The catalog is not populated through reflection, configuration strings, plugin loading or test fixtures. Normal-startup registration and explicit registry construction must be intentional and reviewed together.

Current composition snapshot: Ownership=false, Permission=false, ExecutionState=false, AuditSink=false, AuditDelivery=false, Reconciliation=false, Verification=false, ExecutionPolicy=false, ToolHandlers=false, TrustedExecution=false; **CompositionComplete=false — NOT READY**. Step 14P final activation also requires independent runtime evidence and security approval. Flags or a synthetic matching test catalog cannot enable ToolHandlers.

**LIVE DATABASE EVIDENCE: NOT APPLICABLE.** There are no production handlers and no handler-specific live DB behavior to exercise. No live harness was run. Handler source presence or offline fixtures are not production PASS evidence.

## Remaining blockers and validation

Design and independently review each real handler, exact ID/capability/permission pairing and side-effect class; review explicit registry composition, scoped DI registration and exact catalog membership; then verify all independent identity, permission, approval, execution policy, durable state, audit, reconciliation, verification, trusted execution and Step 14P activation gates. Any future file, process, network or provider access needs separate security and operational evidence. Production execution remains disabled.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
