# Step 16A — production composition implementation plan

## Dependency graph and order

The observational `ProductionRuntimeCompositionInspector` computes Ownership from `IAuraDbContext`, authenticated-subject mapping, current-user context, ownership source and exact scope validator. Permission depends on its exact grant source and owned scope. ExecutionState depends on the exact durable store and an `IDbContextFactory<AuraDbContext>`. AuditSink and AuditDelivery separately depend on the context factory, and delivery also depends on the sink. TrustedExecution requires all nine other composition fields plus one exact reviewed scoped executor; it is outside Steps 16A–16E.

Order: **16A plan → 16B ownership → 16C permission → 16D durable state → 16E audit sink/delivery**. Before any catalog entry, inspect the implementation, unique scoped DI shape, full constructor graph, target migration baseline, applicable live evidence and unresolved failure semantics. Explicit type entries only; no assembly discovery. Re-run the relevant gated evidence command after a registration when a verified disposable target and fixtures are available. Capture sanitized result, target identity, version, time and cleanup separately; a PASS never sets runtime readiness on its own.

## Registration and no-activation rules

Registration-only work could be safe while all five runtime flags remain false, provided no endpoint, worker, handler, policy, trusted executor or direct invocation path is added. Yet a registration must not be made just to turn an inspector Boolean true. The current record lacks independently reviewable live run artifacts and still has unresolved ownership token/isolation, permission revocation, historical migration, state factory, audit replay and transaction decisions. Steps 16B–16E must leave a field false if those conditions remain unresolved. Existing catalogs are empty: 16B needs exact ownership context, subject mapping, source and validator entries; 16C needs `ToolPermissionPersistenceSource`; 16D needs `ActionExecutionStatePersistenceStore`; 16E needs `SecurityAuditPersistenceSink` and `SecurityAuditOutboxDispatcher`. Each entry requires its exact normal-startup scoped registration and a separate production review.

Expected safe checkpoint snapshots with current evidence: after 16A, 16B, 16C, 16D and 16E, **all ten fields remain false**. Best-case future independent reviews could make Ownership, then Permission, then ExecutionState, then AuditSink and AuditDelivery true. Reconciliation, Verification, ExecutionPolicy, ToolHandlers and TrustedExecution remain false through this programme, so `CompositionComplete=false` regardless. `ProductionAdaptersEnabled`, `ToolExecutionEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` remain false. No final approval or activation gate is introduced.

## Rollback and fail-closed criteria

Stop a checkpoint if build, offline checks or `git diff --check` fails, if a required DI graph is ambiguous, if a live command is unsafe/unavailable, if evidence is incomplete, or if a catalog entry would outrun reviewed evidence. Revert only the checkpoint's own proposed registration/catalog change before proceeding; preserve existing runtime behavior. Unknown identity/scope/grant/state or ambiguous DB/audit result must fail closed. Do not auto-migrate or infer exactly-once side effects from an outbox PASS.

Validation after each checkpoint: `dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false`; `dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false`; `git diff --check`; `git status --short`; `git diff --stat`.
