# Step 15F — reconciliation production evidence and registration review

## Purpose, scope and current verdict

This step adds an explicit disposable-PostgreSQL evidence command for the existing reconciliation reader, advisory workflow and assessment table. It reuses Step 13I's generated ownership/action/approval/reserved-state fixture and exact cleanup helpers. It does not register reconciliation in the API, add a production assessment writer, mutate production execution state, run a tool, apply a migration or enable runtime flags. **Reconciliation: false. CompositionComplete: false — NOT READY.** Harness existence and offline PASS are not live PostgreSQL PASS evidence. No live run is claimed here.

## Current reconciliation model

`ExecutionReconciliationPersistenceSource` is the only concrete durable `IExecutionReconciliationSource`. It is unregistered in normal API startup and requires `IDbContextFactory<AuraDbContext>`. `GetAsync` opens a RepeatableRead transaction, reads `ActionExecutionStates` without tracking by exact ActionId and internal UserId/ConversationId/ProjectId, checks a consumed approval, nonempty execution ID, canonical tool, status/version/timestamp shape, active user, owned project, project conversation, matching action and action approval, then returns a bounded snapshot with `Reason.Unknown`. It accepts valid Reserved/version 1 and valid terminal/version 2 snapshots; it does not itself decide eligibility. Missing, foreign, malformed or database-error state returns null; cancellation propagates. It makes no database update, insert or delete.

`ExecutionReconciliationService` is the advisory assessment workflow. It validates the caller's scope before and after reading/assessing, compares the source snapshot's action/scope/tool/approval/execution/version with its embedded durable state, rejects Ready as NotEligible and Succeeded/Failed as AlreadyResolved, and issues bounded evidence only for Reserved. The conservative policy returns NeedsManualReview. `SafeToMarkFailed` is an advisory label, not a transition. Unknown reason or disposition fails closed. The workflow neither calls `TryCompleteAsync` nor writes `ExecutionReconciliationAssessments`. Its audit observation uses the supplied writer; it is not a transactionally coupled assessment write.

`ExecutionReconciliationAssessmentRecord` is a historical advisory table model with exact action/user/conversation/project/tool/approval/execution IDs, Reserved/version 1, bounded reason (0–5), bounded disposition (0 or 1), UTC assessment time and a generated assessment ID. The EF check constraint enforces those bounded values. The primary key is the assessment ID; there is no unique action or execution index and no single-use guarantee. Individual foreign keys do not establish every composite binding. No production assessment persistence adapter exists today.

## Live evidence command and prerequisites

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-reconciliation-evidence
```

Required process environment:

- `AURA_LIVE_PRODUCTION_RECONCILIATION_EVIDENCE=true`
- `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true`
- `ConnectionStrings__DefaultConnection` with a host, username and database named `aura_disposable_...`

Both acknowledgement values must be exactly lowercase `true`. Missing either prints `PRODUCTION RECONCILIATION EVIDENCE: SKIPPED` and returns before reading the connection string, creating a context or accessing PostgreSQL. SKIPPED is not PASS. Malformed or non-prefixed connections fail before the DB runner. These syntactic checks do not prove disposability; the operator must verify the actual host, database, authorization and write role. No permanent IDs or credentials are embedded in source.

The disposable DB needs the reviewed Step 15B core/security baseline, reconciliation assessment table, execution-state table, audit outbox and all fixture dependencies already present. The command checks relevant table existence and migration entries but does not repeat the full Step 15B schema audit. It never applies migrations or creates schema.

## What the harness observes

The live verifier creates generated user/project/conversation/action/approval, a Reserved/version 1 durable state and a pending outbox fixture. It reads the real reconciliation source, uses the real ownership persistence source with a **test-only fixed generated current user**, and runs `ExecutionReconciliationService` with the conservative policy. This is not Supabase authentication or production ownership-composition evidence.

It checks the exact Reserved action/scope/tool/approval/execution/version and Unknown reason; issues NeedsManualReview evidence; checks that real source and service reads do not mutate durable state; rejects foreign user/conversation/project/action lookups; rejects altered tool/approval/execution/action and inconsistent version snapshots at the service boundary; and observes that separate Succeeded/version 2 and Failed/version 2 fixtures are AlreadyResolved. It issues two assessments of the same Reserved state with distinct IDs and unchanged durable state. A separate test-only EF mapping writes both bounded evidence records directly to `ExecutionReconciliationAssessments`, rereads exact IDs/fields/reason/disposition/time, verifies the durable state remains unchanged, and checks PostgreSQL rejects unknown reason and disposition values under the named state constraint. This proves the current table accepts and bounds those generated rows; it **does not prove a production assessment-write service**, because none exists.

The stale-version check rejects an internally inconsistent source snapshot (`snapshot.Version` differs from `state.Version`) and checks current terminal state through the real source. There is **no expected-version input or compare-and-swap at assessment persistence**. A state transition after the source's read and before evidence issuance could yield historically stale advisory evidence. Live PASS does not close this race. Before production activation, define whether historical observation is acceptable or add a separately reviewed version-bound write/revalidation path. Do not interpret an advisory assessment as current permission to mutate execution state.

The source does not accept external tool, approval or execution parameters; it reads those values from the durable row and validates the ownership/action/approval chain. The workflow checks a returned snapshot against its embedded state. The harness's altered-binding cases exercise that workflow boundary; they do not establish a database-level composite foreign key for every column. No arbitrary text or payload is written.

Cleanup deletes only the generated assessment IDs first, then uses the existing generated fixture cleanup for exact event/outbox/action-state/approval/action/conversation/project/user IDs. It verifies generated assessment, event, outbox and user rows are gone. Cleanup failure makes the result FAIL. A process crash or DB connection loss can leave fixtures; use only a verified disposable database. The harness does not snapshot every unrelated row, but its delete predicates target generated IDs and never broadly delete a table.

Success emits ten bounded PASS categories—DB CONNECTIVITY, SCHEMA CHECK, FIXTURE SETUP, VALID RESERVED ASSESSMENT, STALE VERSION, TERMINAL STATE, NEGATIVE BINDINGS, REPEATED ASSESSMENT, ASSESSMENT PERSISTENCE and CLEANUP—then `RESULT: PASS`. The preceding limits define exactly what those categories mean. Failure prints one bounded failing category where known and `RESULT: FAIL`; no connection string, password, raw exception, row contents or stack trace is printed. A PASS is evidence about one disposable database run, not registration or activation approval.

## Manual PowerShell operation

Use a fresh PowerShell session at the repository root. Verify disposable host/database identity out of band, review Step 15B evidence, and use a role scoped to required fixture writes and catalog/history reads. Configure TLS for that host without weakening certificate validation. Do not save credentials to a file.

```powershell
Add-Type -Path (Join-Path (Get-Location) 'security-checks/bin/Debug/net10.0/Npgsql.dll')
$securePassword = Read-Host 'Disposable DB write-role password' -AsSecureString
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
    $env:AURA_LIVE_PRODUCTION_RECONCILIATION_EVIDENCE = 'true'
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-reconciliation-evidence
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection',
        'AURA_LIVE_PRODUCTION_RECONCILIATION_EVIDENCE',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

The password and connection string necessarily exist in process memory while running. Clearing the environment and BSTR does not guarantee managed string erasure. Capture the command, time, verified disposable DB identity, sanitized categories and cleanup result in the approved evidence record, without credentials or row contents.

## Production registration review and remaining blockers

Normal `Aura.Api/Program.cs` has scoped `AuraDbContext` and `IAuraDbContext`, but no `IDbContextFactory<AuraDbContext>` or `IExecutionReconciliationSource` registration. The source's factory dependency needs deliberate normal-startup review. The inspector requires exactly one scoped `IExecutionReconciliationSource` descriptor implemented by `ExecutionReconciliationPersistenceSource` and that exact type in `ReviewedReconciliation`. The catalog is empty. Duplicate, factory, instance or wrong-lifetime registrations cannot assert readiness. Live evidence alone changes neither DI nor catalog. No reconciliation service, scope-validator, policy or assessment writer is activated by this step.

Before Reconciliation can be marked ready, review the target's Step 15B migration baseline, live results, authenticated ownership composition, complete DI graph, composite binding integrity, stale-read policy, repeated-assessment/idempotency policy, actual assessment persistence path and audit provenance. Decide how historical observations are retained and how a concurrent state change is represented. Only a later reviewed change can add production registration and catalog membership. Ownership, Permission, ExecutionState, AuditSink, AuditDelivery, Verification, ExecutionPolicy, ToolHandlers, TrustedExecution and Step 14P final activation remain independent blockers. All ten production composition fields and CompositionComplete remain false; tool execution, Safe File Access and Cloud AI remain disabled.

## Offline validation

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```

The normal security suite uses an injected fake live runner, pure application checks and EF metadata. It makes no PostgreSQL connection.
