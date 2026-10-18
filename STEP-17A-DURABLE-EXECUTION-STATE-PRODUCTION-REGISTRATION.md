# Step 17A — durable execution-state production registration

## Purpose and decision

**Registration deferred; `ExecutionState=false`.** This checkpoint reviewed the exact durable store, its dependency graph, the Step 15B/15D evidence and the Step 16D decision. The operator reports that the production execution-state harness passed manually on a verified disposable PostgreSQL target and that the process environment and password were cleaned afterward. This agent did not run the live command or receive a sanitized target-bound run record. More importantly, no Step 15B live baseline result or historical core-schema provenance approval was supplied. The reported execution PASS does not settle those separate requirements.

## Current architecture and exact registration design

`IActionExecutionStateStore` is the application contract. The sealed `ActionExecutionStatePersistenceStore` is its concrete PostgreSQL implementation. Its only constructor dependency is `IDbContextFactory<AuraDbContext>`. It creates a context per operation, uses transactions and conditional updates, and writes a pending audit-outbox intent in the same successful transition transaction. It does not execute a tool or deliver the audit event. A scoped store is the exact lifetime required by the Step 14H inspector; the factory may be singleton when configured with singleton-compatible EF options. The store does not capture an HTTP or other scoped dependency.

Normal API startup currently uses `AddDbContext<AuraDbContext>` and a scoped `IAuraDbContext` alias. It has no factory or store registration. The smallest future graph is one explicit `AddDbContextFactory<AuraDbContext>` configured with the same `UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector())` options, plus exactly one `AddScoped<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>()`. EF Core's factory registration also exposes a scoped context; adding it alongside the existing `AddDbContext` can create duplicate `AuraDbContext` descriptors and conflicting options lifetimes. Resolve that by replacing the current context registration with a single factory-based registration and retaining the existing scoped `IAuraDbContext` alias, then test both the context alias and the factory with service-provider validation. The factory should be singleton with singleton-compatible options, and the store scoped. Do not substitute a pooled factory, implementation factory, or instance registration. **No such registration was added in this checkpoint** because the evidence review remains incomplete.

The inspector's exact catalog entry, once independently justified, is `typeof(ActionExecutionStatePersistenceStore)` in `ReviewedExecutionState`. Today that catalog is empty. The inspector already requires one scoped descriptor with that implementation type and catalog membership; its rule was not weakened. The offline Step 17A checks keep the current blocked state explicit and reject an unreviewed exact registration, duplicates and an unavailable substitute.

Registration by itself would not connect to PostgreSQL, migrate, create schema, run a handler, start a worker or enable execution. `DefaultConnection` is required at API startup and missing configuration fails closed. The existing runtime flags default false and final production approval source cannot approve activation. This checkpoint makes no DI, runtime flag, readiness, migration or schema change.

## Migration baseline and live evidence

Step 15B documents five targeted migration IDs and a read-only catalog verifier, but no historical core-schema migration or model snapshot is present. Its document records no live baseline PASS or core-schema provenance approval. The checked-in Step 13C execution-state migration creates the state table, foreign keys, check constraints, and unique approval/execution-ID indexes. The operator-reported live Step 15D PASS confirms its targeted schema check on the tested disposable database, but that check covers only the execution-state migration entry and selected state-table shape. It does not prove the full five-migration history, core tables, and wider key constraints covered by Step 15B, nor how the core schema was originally established. Exact missing evidence: a reviewed Step 15B result for the same target and an approved description of the historical core-schema baseline/provenance. No migration or schema change is justified by the reported result.

The operator-reported live output was:

```text
DB CONNECTIVITY: PASS
SCHEMA CHECK: PASS
FIXTURE SETUP: PASS
INITIAL RESERVATION CONCURRENCY: PASS
APPROVAL REUSE: PASS
EXECUTION ID REUSE: PASS
STALE VERSION: PASS
COMPLETION CONCURRENCY: PASS
NEGATIVE BINDINGS: PASS
CLEANUP: PASS
RESULT: PASS
```

The successful scenarios support transaction, conditional version update, unique approval/execution ID, exact supplied scope-binding, and cleanup behavior on that target. They do not prove that every supplied binding belongs to the same underlying action and approval. `ActionExecutionStates` has separate foreign keys for `ActionId`, `UserId`, `ConversationId`, `ProjectId`, and `ApprovalId`; the store validates request values and compares them with its own row but does not join `AIActions`, `ActionApprovals`, `Conversations`, and `Projects` before creating the initial row. Distinct existing rows could satisfy all individual foreign keys while forming an inconsistent chain. Step 14H explicitly requires composite binding integrity and approval lifecycle review. A trusted caller may validate these first, but that production graph is not registered here and the state store itself cannot establish the chain. An exact fail-closed source of that validation or a reviewed database invariant remains necessary before catalog approval.

The store commits reservation and completion with an outbox intent in one database transaction. A failure around `CommitAsync` can return `Unavailable` even if the transaction committed. The durable row and execution ID allow later state inspection and the unique indexes prevent a second reservation; however, callers must not treat `Unavailable` as proof of rollback or start a fresh external effect without reconciliation. This is a recovery-contract review, separate from exactly-once external tool effects. Step 14H does not require exactly-once effects for `ExecutionState`, but it does require an assessment of this ambiguity. The reported live PASS does not inject an ambiguous commit failure. No handler or trusted executor is registered, so this checkpoint cannot make a false external-effect claim.

An authorized operator must first identify a genuinely disposable PostgreSQL target, verify its host, database, scoped role and migration history out of band, and review the Step 15B read-only baseline result and historical core schema. The write-capable Step 15D harness requires generated-fixture insert/update/delete permissions and must never target a shared or unknown database. Its gates require exact lowercase `true` for `AURA_LIVE_PRODUCTION_EXECUTION_STATE_EVIDENCE` and `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE`, plus a process-only `ConnectionStrings__DefaultConnection` whose database name begins `aura_disposable_`. Those syntax checks do not prove the target is disposable.

Use this secure PowerShell sequence from the repository root, only after the target and role have been independently verified. Configure TLS appropriate to the host without weakening certificate validation. Do not record the connection string or password.

```powershell
Add-Type -Path (Join-Path (Get-Location) 'security-checks/bin/Debug/net10.0/Npgsql.dll')
$securePassword = Read-Host 'Verified disposable DB write-role password' -AsSecureString
$passwordBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
$builder = $null
try {
    $builder = [Npgsql.NpgsqlConnectionStringBuilder]::new()
    $builder.Host = Read-Host 'Verified disposable DB host'
    $builder.Database = Read-Host 'Disposable DB name (aura_disposable_...)'
    $builder.Username = Read-Host 'Scoped DB write role'
    $builder.Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordBstr)
    $builder.Timeout = 5
    $env:ConnectionStrings__DefaultConnection = $builder.ConnectionString
    $env:AURA_LIVE_PRODUCTION_EXECUTION_STATE_EVIDENCE = 'true'
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-execution-state-evidence
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection',
        'AURA_LIVE_PRODUCTION_EXECUTION_STATE_EVIDENCE',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

Capture a sanitized record of the time, commit, approved target identity, all ten PASS categories (including `CLEANUP: PASS`) and `RESULT: PASS`, or the bounded FAIL/SKIPPED result. Inspect generated fixtures if cleanup fails or the process stops unexpectedly. The command does not apply migrations. A PASS applies only to that reviewed target and run; it does not prove exactly-once external tool effects.

## Composition, remaining blockers and rollback

The current ten-field snapshot is `Ownership=false`, `Permission=false`, `ExecutionState=false`, `AuditSink=false`, `AuditDelivery=false`, `Reconciliation=false`, `Verification=false`, `ExecutionPolicy=false`, `ToolHandlers=false`, `TrustedExecution=false`; `CompositionComplete=false`. `ToolExecutionEnabled`, `ProductionAdaptersEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` remain false. Activation is denied by the unavailable final approval source and missing composition.

Before registration, obtain the same target's Step 15B baseline result and core-schema provenance approval; review an exact cross-entity action/approval/scope and approval-lifecycle validation path; define fail-closed handling for ambiguous commit outcomes; and validate the single factory/context DI graph. The user-reported live scenarios need no repetition merely to prove the same scenarios, but retain a sanitized target-bound record for independent review. Then approve the exact implementation and catalog entry. Other composition dimensions and final approval remain separate blockers. If a later registration fails review, remove that registration and its catalog entry, rerun the offline suite, and leave flags disabled. Step 17A added no production registration, so its rollback consists only of removing this document and its offline checks.

## Validation

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
git status --short
git diff --stat
```

Offline PASS is not live PostgreSQL evidence. Harness existence is not PASS evidence.
