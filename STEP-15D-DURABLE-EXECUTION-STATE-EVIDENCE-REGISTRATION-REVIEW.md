# Step 15D — durable execution-state evidence and registration review

## Purpose and scope

This step exposes the existing write-capable `LiveExecutionStateVerification` fixture path under a separate production-evidence command and strengthens its negative-binding assertions. It collects evidence only for a known disposable PostgreSQL database. It does not register the store in the API, enable a tool, add a worker, apply a migration, or change a schema. **Harness existence and offline PASS are not live PostgreSQL PASS evidence.** No live run is claimed by this document.

## Durable state and exact predicates

`IActionExecutionStateStore` is implemented by `ActionExecutionStatePersistenceStore`, which takes `IDbContextFactory<AuraDbContext>` and creates a context per operation. The contract accepts a trusted `ActionIdentifier`, server-resolved `ActionScope` (internal user, conversation, project), canonical tool, approval, expected version and trusted-workflow execution attempt ID. This store coordinates execution state; it does not run a handler or create an external effect.

`TryReserveInitialAsync` starts a transaction, inserts Ready/version 0 if the action has no row, then calls the reservation path. Reservation checks exact action, user, conversation, project, tool and approval binding; its conditional database update also requires Ready, version 0, null execution ID, unconsumed approval, and null reservation/completion timestamps. Exactly one affected row returns success, setting Reserved/version 1, the execution ID, consumed approval and reservation timestamp. The transaction also stages a pending security-audit outbox row. A duplicate initial insert is rejected by the action primary key; a reused approval or execution ID is rejected by separate unique indexes. Unique violations return a denied result, and the transaction rolls back. The exact status can vary between an application conflict and a uniqueness conflict; the harness requires a fail-closed conflict and no second state row.

Completion checks the same scope/tool/approval binding plus the exact execution ID, Reserved/version 1, consumed approval, existing reservation timestamp and null completion timestamp in a conditional update. One affected row changes the state to Succeeded or Failed/version 2 and stages the corresponding pending outbox row in the transaction. Wrong bindings, stale versions, duplicate completion and terminal reopening are denied. Cancellation is propagated. The application policy alone is not a concurrency boundary: the database conditional updates, action primary key, unique approval index and filtered unique execution-ID index provide the durable boundary. No retry loop is present.

The live harness creates a generated user, project, conversation, three actions and two approvals. It races two initial reservations, verifies one winner and version 1, tests approval and execution-ID reuse on other generated actions, stale reservation, wrong scope/tool/approval bindings for reservation and completion, wrong execution ID, cancellation, two competing completions, terminal version 2, duplicate completion and unchanged completion timestamp. It reads the generated state after transitions. The initial Ready row is created within the reservation transaction; it is not an independently committed Ready fixture. The harness's targeted schema check confirms the execution-state migration history entry, table, critical columns, constraints and unique indexes; Step 15B is the broader migration-baseline review.

## Live command and safety gates

Run only against a verified disposable database that the operator is authorized to mutate. The evidence command is:

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-execution-state-evidence
```

The process environment must contain exactly lowercase `true` for both `AURA_LIVE_PRODUCTION_EXECUTION_STATE_EVIDENCE` and `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE`. If either is absent, the command prints `PRODUCTION EXECUTION STATE EVIDENCE: SKIPPED` and returns before reading the connection string or creating a context. SKIPPED is not PASS. `ConnectionStrings__DefaultConnection` must parse with a host, username and nonempty database name beginning `aura_disposable_`. These syntactic and acknowledgement gates do not prove disposability; the operator must verify the actual host, database and write-capable role. No external fixture IDs are required.

The command needs a database with the reviewed Step 15B baseline and the execution-state, audit-outbox and core tables already present. It never migrates or creates schema. It writes only generated fixtures and associated state/outbox rows. Cleanup runs after any fixture setup attempt, deletes dependent rows by generated action IDs and user ID before the generated approvals/actions/conversation/project/user, then verifies those generated rows are absent. It has no broad delete, reset or truncate. A cleanup failure makes the overall result FAIL. A process crash or connection loss can leave generated fixtures requiring operator inspection; do not use a shared or non-disposable database. Rows outside those generated IDs are not intentionally targeted; the harness does not snapshot every unrelated row.

Success prints ten bounded `...: PASS` categories and `RESULT: PASS`: connectivity, schema, fixture setup, initial reservation concurrency, approval reuse, execution-ID reuse, stale version, completion concurrency, negative bindings and cleanup. Failure prints a bounded failed category and `RESULT: FAIL`; raw exceptions, connection values and row contents are suppressed. Output does not identify which generated row failed. A live PASS is evidence only for the tested database and this harness run.

## Manual PowerShell run

In a fresh PowerShell session at the repository root, verify the host and disposable DB identity out of band. Use a DB role with only the permissions needed for these generated-fixture writes and catalog/history reads. Configure TLS for that host without weakening certificate validation. Do not save the connection string or password to a file.

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

The password and connection string necessarily exist in process memory during the run. Clearing the environment and BSTR does not guarantee managed string erasure. Capture the command, time, sanitized PASS/FAIL categories, reviewed DB identity and cleanup result in the operator's approved evidence record, without recording credentials or row contents.

## Production registration review and current readiness

Normal `Aura.Api/Program.cs` registers scoped `AuraDbContext` and `IAuraDbContext`, but not `IDbContextFactory<AuraDbContext>` or `IActionExecutionStateStore`. The durable store's factory dependency therefore needs a deliberate normal-startup registration review before it could resolve. The inspector requires exactly one scoped `IActionExecutionStateStore` descriptor with implementation type exactly `ActionExecutionStatePersistenceStore`, and membership in `ReviewedExecutionState`; that catalog is empty. A factory/instance registration, duplicate registration or other lifetime does not satisfy the inspector. The API still uses `UnavailableRuntimeCapabilityReadiness` and has no trusted execution registration. Step 15D does not change any of these facts.

**ExecutionState: false. CompositionComplete: false — NOT READY.** The full composition snapshot remains Ownership, Permission, ExecutionState, AuditSink, AuditDelivery, Reconciliation, Verification, ExecutionPolicy, ToolHandlers and TrustedExecution all false. A reviewed live PASS is necessary but insufficient: the target's Step 15B baseline and historical core migration provenance need review; cross-entity action/approval/scope integrity and approval lifecycle remain separate concerns; the factory and exact scoped store registration must be designed, resolved and reviewed; the catalog must be updated only after independent production review; wider capabilities and the Step 14P final approval must remain satisfied. The store's reservation boundary does not guarantee exactly-once external tool effects.

## Offline validation

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```

The normal security suite uses a synthetic runner and pure policy checks for Step 15D; it does not connect to PostgreSQL. Production flags, Safe File Access, Cloud AI and execution remain disabled.
