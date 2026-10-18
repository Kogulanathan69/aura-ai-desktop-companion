# Step 15E — audit persistence and recovery evidence / registration review

## Purpose, scope and current verdict

This step adds an explicit production-evidence entry point to the existing Step 13I audit/outbox live harness and extends its recovery and duplicate-ID scenarios. It performs no automatic database operation and makes no production registration or readiness change. **AuditSink: false. AuditDelivery: false. CompositionComplete: false — NOT READY.** Harness existence and offline PASS are not live PostgreSQL PASS evidence. No live run is claimed here.

## Audit event, outbox and sink

`SecurityAuditPersistenceSink` is the concrete `IAuditEventSink`. It validates a bounded `AuditEventRecord`, maps its event type, outcome, UTC timestamp, user/conversation/project scope and optional action/tool/approval/execution/verification/reconciliation/permission IDs, then writes exactly one `SecurityAuditEvents` row. `Accepted` follows one successful `SaveChangesAsync`. Duplicate event IDs are accepted only if every stored typed field matches the requested record, including PostgreSQL-microsecond timestamp equivalence. A mismatched duplicate returns `Failed`; other persistence errors return bounded `Unavailable`, and cancellation propagates. The direct sink creates **no outbox row** and is not in the execution-state transaction.

`ActionExecutionStatePersistenceStore` writes a bounded `SecurityAuditOutbox` intent in the **same transaction** as each successful reservation or completion transition. The outbox has a stable unique `AuditEventId`, exact execution action/user/conversation/project/tool/approval/execution bindings, event type/outcome/time, delivery state, attempt count and delivery time. The action-state transaction either commits both its state change and outbox intent or rolls back both. A failed direct event write does not undo a previously committed execution state; outbox delivery remains a separate operation. The direct sink is not a transactionally coupled state writer.

`SecurityAuditOutboxDispatcher` implements `IAuditOutboxDispatcher` as an explicit one-item operation keyed by outbox ID. It opens its own transaction, conditionally locks a Pending/zero-attempt/null-delivery-time row, rereads it, restores a bounded audit event and checks its exact execution-state binding. It calls the injected sink once. `Accepted` commits Delivered/attempt 1/non-null UTC `DeliveredAt`; `Failed` commits Failed/attempt 1/null `DeliveredAt`. `Unavailable` exits without commit, retaining Pending/attempt 0/null timestamp for a later **explicit** dispatch. Cancellation propagates. Delivered and Failed are terminal to this dispatcher; a repeated dispatch returns NotPending. There is no scan, worker, schedule, polling or automatic retry.

Step 13J's hardened `CK_SecurityAuditOutbox_Delivery` constraint requires exactly Pending/0/null, Delivered/positive/non-null, or Failed/positive/null. The dispatcher writes 1 for its only attempt. The outbox's unique `AuditEventId` and event table's primary key reject duplicate identities. Stable IDs plus exact duplicate comparison make the database sink idempotent for this bounded record. The dispatcher row lock and conditional update bound concurrent attempts. They do not prove exactly-once external delivery. A sink commit can succeed before the dispatcher commits Delivered; a crash or ambiguous `Unavailable` can leave Pending and cause an explicit later replay. The database sink's exact-ID comparison handles that replay only when the persisted row matches. A different external sink would need its own idempotency and recovery review.

## Live evidence command and gates

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-audit-evidence
```

Required process environment:

- `AURA_LIVE_PRODUCTION_AUDIT_EVIDENCE=true`
- `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true`
- `ConnectionStrings__DefaultConnection` with a host, username and database named `aura_disposable_...`

Both acknowledgement values must be exactly lowercase `true`. Missing either prints `PRODUCTION AUDIT EVIDENCE: SKIPPED` and returns before reading the connection, creating a context or accessing PostgreSQL. SKIPPED is not PASS. The connection parser rejects malformed or non-prefixed database names before invoking the live runner. The name and acknowledgement are only syntactic safeguards: the operator must independently verify the host, database, write role, disposal policy and authorization. No permanent fixture IDs or source credentials are required.

The database must already contain a reviewed core schema and the five targeted migrations through Step 13J. The harness checks connectivity, relevant tables and migration entries, then uses generated unique IDs for each user/project/conversation/action/approval/execution/outbox/event fixture. It does not apply migrations or modify schema. Step 15B remains the broad migration-baseline review.

## Positive and negative scenarios

The reused live harness exercises real sink persistence and exact event/outbox ID and scope/action/tool/approval/execution binding. It checks one successful delivery, one Failed terminal delivery, Unavailable leaving Pending, a subsequent explicit successful recovery of that Pending row, terminal redispatch denial, exact duplicate event acceptance, mismatched duplicate event rejection, a database rejection of duplicate outbox `AuditEventId`, concurrent dispatch with one sink call, cancellation rollback, malformed event rejection and separate wrong-binding substitutions. It verifies state/attempt/timestamp shapes and event-row count after each scenario. A fake Failed or Unavailable sink is used only to force those dispatcher branches; successful persistence uses `SecurityAuditPersistenceSink` against PostgreSQL. The harness does not run a tool or provider.

Cleanup runs in `finally`, deletes only generated event/outbox/action-state/approval/action/conversation/project/user IDs in dependency order and verifies generated event, outbox and user rows are absent. Cleanup failure prevents overall PASS. A process crash or lost DB connection can leave fixtures; only a verified disposable database is suitable. The harness does not take a full snapshot of unrelated rows, but its cleanup predicates target generated IDs rather than broad tables.

On success, bounded output is `DB CONNECTIVITY`, `SCHEMA CHECK`, `AUDIT PERSISTENCE`, `EVENT/OUTBOX BINDING`, `DELIVERY SUCCESS`, `DELIVERY FAILURE`, `RECOVERY/REPLAY`, `IDEMPOTENCY`, `CLEANUP` as PASS, followed by `RESULT: PASS`. Failures emit bounded `RESULT: FAIL`, plus `CLEANUP: FAIL` when cleanup explicitly fails. A failure before fixture setup has no cleanup result. No connection, password, raw exception, audit payload, row dump or stack trace is printed. The command does not claim an individual category passed when the combined runner failed.

## Manual PowerShell operation

Use a fresh PowerShell session from the repository root. Verify the disposable host and database out of band, ensure the Step 15B baseline is reviewed, and use a role scoped to required fixture writes and catalog/history reads. Configure TLS as required by that host without weakening certificate validation. Do not save credentials to source or an output file.

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
    $env:AURA_LIVE_PRODUCTION_AUDIT_EVIDENCE = 'true'
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-audit-evidence
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection',
        'AURA_LIVE_PRODUCTION_AUDIT_EVIDENCE',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

The password and connection string necessarily exist in process memory during execution. Clearing the environment and BSTR cannot guarantee erasure of managed string copies. Capture the command, time, verified disposable DB identity, sanitized PASS/FAIL output and cleanup result in the approved operator record, without recording credentials or audit rows.

## Production registration review and remaining blockers

Normal `Aura.Api/Program.cs` registers scoped `AuraDbContext` and `IAuraDbContext`, but does not register `IDbContextFactory<AuraDbContext>`, `IAuditEventSink` or `IAuditOutboxDispatcher`. Both concrete audit services require the context factory; the dispatcher also requires the sink. A later normal-startup DI review must deliberately supply those dependencies and resolve the complete graph. The inspector requires exactly one scoped `IAuditEventSink` implemented by `SecurityAuditPersistenceSink` and exactly one scoped `IAuditOutboxDispatcher` implemented by `SecurityAuditOutboxDispatcher`, each independently present in its reviewed catalog. Both catalogs remain empty. Duplicate, factory, instance or wrong-lifetime registrations do not satisfy those composition predicates. Live PASS alone changes neither catalog nor DI.

AuditSink needs reviewed live evidence, transaction/failure-window and append-only/retention/tamper decisions, exact scoped registration and catalog review. AuditDelivery needs reviewed live concurrent/replay/rollback evidence, an explicit operational recovery policy, external-effect idempotency decision, exact scoped registration and catalog review. Step 15B baseline and historical core-schema provenance remain separate. `UnavailableRuntimeCapabilityReadiness` remains in normal startup. Ownership, Permission, ExecutionState, Reconciliation, Verification, ExecutionPolicy, ToolHandlers, TrustedExecution and Step 14P final approval are independent blockers. All ten production composition fields and CompositionComplete remain false; production execution, Safe File Access and Cloud AI remain disabled.

## Offline validation

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```

The normal suite uses an injected synthetic runner and EF model metadata only. It does not access PostgreSQL.
