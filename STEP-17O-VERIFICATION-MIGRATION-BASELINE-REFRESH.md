# Step 17O — verification migration and same-target baseline refresh

## Purpose and current evidence

Review the Step 17N migration, update the read-only Step 15B catalog verifier to six exact migration IDs, and provide a controlled operator-only application path for the previously verified disposable PostgreSQL target. **OPERATOR-REPORTED LIVE RESULT: PASS.** The operator supplied sanitized `VERIFICATION MIGRATION APPLY: PASS` and the subsequent six-ID baseline `RESULT: PASS` on the same verified disposable target. This Codex process did not connect to the database, apply the migration, or run the live baseline. Offline checks are separate from the operator's live evidence.

Step 17K's earlier operator-reported PASS was for `aura_disposable_test`, database OID `24576`, system identifier `7696813047573034172`, with the **first five** targeted migrations. That record establishes a bounded prior state, not application of the sixth migration. Historical core-schema creation provenance remains partially unresolved as Step 17K states.

## Migration review

`20261018000000_AddTrustedVerificationFoundation` has exactly three Up operations on `ExecutionVerificationAssessments`: add nullable `bigint` `ExecutionVersion` with no default, drop `CK_ExecutionVerificationAssessments_State`, then add the revised check. Null preserves existing advisory rows. The revised check still requires terminal execution status `2` or `3` and outcome `1` or `2` (Failed/Inconclusive); it permits the old null-version `None/0` proof pair, or version `2` with `None/0` or reserved `OwnedProjectMetadata/1`. It **does not permit Verified** or manufacture independent proof. The EF configuration and migration expression agree. There is no seed, data rewrite, broad SQL, unrelated table change, index change or startup schema mutation.

Down drops the revised check, restores the prior check and drops only `ExecutionVersion`. It is structurally scoped, but applying Down after any new `OwnedProjectMetadata/1` row would fail the restored check. A rollback after new proof-source rows exist requires a separate retention decision; do not delete rows to force Down. The migration source was written explicitly because this repository has no EF model snapshot; offline operation inspection does not prove live application safety or historical core-schema provenance.

## Updated read-only baseline

`ProductionMigrationBaselineEvidence` now requires the six IDs in order, ending with `20261018000000_AddTrustedVerificationFoundation`. It requires `ExecutionVerificationAssessments.ExecutionVersion` to be nullable `int8` and checks the updated validated constraint for legacy null-version/None/0, terminal version 2, and the bounded proof-source/version pair. The prior required tables, keys, foreign keys, unique indexes and checks remain. It still performs catalog and history reads only and does not apply a migration. The prior Step 17K PASS will **not** satisfy this six-ID verifier before the new migration is applied.

## New operator evidence and transport boundary

Before application, the operator's read-only check on `127.0.0.1:5433` confirmed `aura_disposable_test`, database OID `24576`, system identifier `7696813047573034172`, exactly the first five applied migrations and no sixth migration. `ExecutionVersion` was absent and the old verification constraint remained. The first attempt failed before application because the local portable server does not support SSL. With the reviewed loopback-only gate, the operator subsequently reported `VERIFICATION MIGRATION APPLY: PASS` on that target. The read-only verifier then reported `DB CONNECTIVITY: PASS`, `MIGRATION HISTORY: PASS`, `MIGRATION BASELINE: PASS`, `CORE TABLES: PASS`, `KEY CONSTRAINTS: PASS`, `SECURITY SCHEMA: PASS`, and `RESULT: PASS`. Since the verifier requires exactly six ordered IDs, nullable `int8` `ExecutionVersion`, and the updated validated verification check, this is bounded same-target evidence for those current schema properties. It is not a raw catalog dump or proof of historical core-schema creation provenance. The operator reported that credentials and process environment variables were cleared after the run.

The migration evidence command still accepts `SSL Mode=VerifyFull` as its normal transport. It accepts `SSL Mode=Disable` only with `AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE=true`, the existing disposable gate, exact `127.0.0.1` or `::1` host, port `5433`, exact database name, positive expected OID/system identifier, matching live identity, exact first-five applied history, and exactly the single expected pending migration. Other SSL modes are rejected. This exception is confined to the explicit security-checks migration command. API startup and production transport policy are unchanged.

## Exact manual operator procedure

Use only the independently verified disposable `aura_disposable_test` target. Confirm host, OID and system identifier against the approved inventory out of band, use a migration role scoped to this disposable database, and review the current five-ID history before beginning. The migration command itself checks the exact database name, operator-supplied OID and system identifier, the exact first-five applied history and the **single** pending sixth migration before invoking EF's `IMigrator.MigrateAsync` for that ID. If the role cannot read `pg_control_system()`, the command fails closed before applying. It never prints a connection string or raw database exception. The separate baseline command checks the resulting history and selected catalog shape. Neither command runs in normal startup or the offline suite.

### A. Normal or remote reviewed target: VerifyFull

From the repository root, in a fresh PowerShell process, run the following. The host and migration role must come from the approved disposable-target inventory. This path requires a trusted TLS certificate and keeps the loopback exception gate unset. If TLS is unavailable on a remote target, stop. The script clears process variables afterward; capture only sanitized output.

```powershell
Add-Type -Path (Join-Path (Get-Location) 'security-checks/bin/Debug/net10.0/Npgsql.dll')
$securePassword = Read-Host 'Verified disposable migration-role password' -AsSecureString
$passwordBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
$builder = $null
try {
    $builder = [Npgsql.NpgsqlConnectionStringBuilder]::new()
    $builder.Host = Read-Host 'Verified disposable DB host from approved inventory'
    $builder.Database = 'aura_disposable_test'
    $builder.Username = Read-Host 'Scoped disposable migration role'
    $builder.Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordBstr)
    $builder.Timeout = 5
    $builder.SslMode = [Npgsql.SslMode]::VerifyFull
    $env:ConnectionStrings__DefaultConnection = $builder.ConnectionString
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    $env:AURA_EXPECTED_DATABASE_OID = '24576'
    $env:AURA_EXPECTED_SYSTEM_IDENTIFIER = '7696813047573034172'
    $env:AURA_APPLY_VERIFICATION_FOUNDATION_MIGRATION = 'true'
    [Environment]::SetEnvironmentVariable('AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE', $null, 'Process')
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --apply-verification-foundation-migration
    $applyExit = $LASTEXITCODE
    if ($applyExit -eq 0) {
        $env:AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE = 'true'
        dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-migration-baseline-evidence
    }
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE',
        'AURA_EXPECTED_DATABASE_OID', 'AURA_EXPECTED_SYSTEM_IDENTIFIER',
        'AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE',
        'AURA_APPLY_VERIFICATION_FOUNDATION_MIGRATION',
        'AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

### B. Verified local disposable loopback: Disable

Use only for the independently verified local `127.0.0.1:5433` disposable target identified above. The `::1` literal is also accepted by the runner when independently verified as the same target. Do not substitute `localhost`, a hostname, a remote IP, another port or another database. The explicit non-TLS gate is required in addition to the disposable and migration gates. Live identity and migration-history checks still execute before `MigrateAsync`.

```powershell
Add-Type -Path (Join-Path (Get-Location) 'security-checks/bin/Debug/net10.0/Npgsql.dll')
$securePassword = Read-Host 'Verified disposable migration-role password' -AsSecureString
$passwordBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
$builder = $null
try {
    $builder = [Npgsql.NpgsqlConnectionStringBuilder]::new()
    $builder.Host = '127.0.0.1'
    $builder.Port = 5433
    $builder.Database = 'aura_disposable_test'
    $builder.Username = Read-Host 'Scoped disposable migration role'
    $builder.Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordBstr)
    $builder.Timeout = 5
    $builder.SslMode = [Npgsql.SslMode]::Disable
    $env:ConnectionStrings__DefaultConnection = $builder.ConnectionString
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    $env:AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE = 'true'
    $env:AURA_EXPECTED_DATABASE_OID = '24576'
    $env:AURA_EXPECTED_SYSTEM_IDENTIFIER = '7696813047573034172'
    $env:AURA_APPLY_VERIFICATION_FOUNDATION_MIGRATION = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --apply-verification-foundation-migration
    $applyExit = $LASTEXITCODE
    if ($applyExit -eq 0) {
        $env:AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE = 'true'
        dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-migration-baseline-evidence
    }
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE',
        'AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE',
        'AURA_EXPECTED_DATABASE_OID', 'AURA_EXPECTED_SYSTEM_IDENTIFIER',
        'AURA_APPLY_VERIFICATION_FOUNDATION_MIGRATION',
        'AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

`VERIFICATION MIGRATION APPLY: SKIPPED`, `INPUT: FAIL` or `VERIFICATION MIGRATION APPLY: FAIL` is not success. A migration may have committed despite a lost response; on any ambiguous failure, inspect exact history and catalog read-only before considering further action. Do not blindly rerun or roll back. A successful application must be followed by `DB CONNECTIVITY`, `MIGRATION HISTORY`, `MIGRATION BASELINE`, `CORE TABLES`, `KEY CONSTRAINTS`, `SECURITY SCHEMA` and `RESULT: PASS` from the updated read-only verifier. Capture the commit, time, three non-secret target identifiers and bounded lines, not credentials or raw rows. The baseline checks selected schema only; the migration's reviewed operations are the source evidence that unrelated tables are not intentionally changed. The sanitized operator output above now records that bounded live PASS.

## Readiness and validation

The same-target six-ID PASS closes the new migration/history and selected current-schema portion of Verification's blockers. There is still no independent proof adapter, real handler, allowing policy, production verification writer registration or reviewed live proof path. `Ownership=false`, `Permission=false`, `ExecutionState=false`, `AuditSink=false`, `AuditDelivery=false`, `Reconciliation=false`, `Verification=false`, `ExecutionPolicy=false`, `ToolHandlers=false`, `TrustedExecution=false`; `CompositionComplete=false`. `ProductionAdaptersEnabled=false`, `ToolExecutionEnabled=false`, `AuditPersistenceEnabled=false`, `SafeFileAccessEnabled=false`, `CloudAiEnabled=false`. Final approval remains unavailable and activation denied.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
git status --short
git diff --stat
```
