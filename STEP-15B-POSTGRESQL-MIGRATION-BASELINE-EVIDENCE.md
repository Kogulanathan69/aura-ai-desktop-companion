# Step 15B — PostgreSQL migration baseline evidence

## Purpose and verdict

This step adds a separate, explicit, read-only catalog verifier for a known disposable PostgreSQL database. It does not run during API startup or the normal offline security suite. **No live PostgreSQL run was performed here.** Harness existence and offline PASS are not live evidence. Production composition remains **NOT READY**.

The existing Step 13K `--live-audit-outbox-hardening` harness checks migration history and selected outbox constraints, but it can apply the hardening migration and writes temporary fixtures. Its execution path is unsuitable for read-only Step 15B; the new verifier uses the same expected migration IDs and PostgreSQL catalog-inspection approach without invoking that harness.

## Repository migration baseline

The repository has these five targeted migrations, in this order:

1. `20261014000000_AddToolPermissionGrants`
2. `20261014000001_AddActionExecutionStates`
3. `20261014000002_AddSecurityPersistenceBundle`
4. `20261014000003_AddSecurityAuditOutbox`
5. `20261014000004_HardenSecurityAuditOutboxDelivery` — latest expected ID.

There is no EF model snapshot or reviewed historical core-schema migration baseline. This verifier requires the disposable database's `__EFMigrationsHistory` to contain exactly those five distinct IDs; missing, duplicate or additional IDs fail. It compares IDs in repository order. The history table has no application timestamp, so a read-only query cannot prove the actual chronological order in which migrations were applied. Matching IDs and schema are evidence of the current state, not a proof of historical deployment safety.

## Live command and inputs

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-migration-baseline-evidence
```

The command requires `AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE=true` and `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true`, each exactly lowercase `true`. If either gate is absent, it prints `PRODUCTION MIGRATION BASELINE: SKIPPED`, returns without reading the connection string, and does not connect. SKIPPED is not PASS evidence.

The only connection input is `ConnectionStrings__DefaultConnection`. It must parse as an Npgsql connection string with a host, username and database whose name begins `aura_disposable_` and has a nonempty suffix. The prefix and acknowledgement are safety gates, not proof that a database is genuinely disposable. The operator must verify the destination and use a read-only database role. No secret is stored in source or printed by the harness.

## Read-only checks and bounded failures

After opening the database, the verifier reads `information_schema` and `pg_catalog` with SELECT statements only. It checks:

- `__EFMigrationsHistory` existence, distinct expected IDs, latest ID, no missing or unknown IDs, and ID ordering against the repository manifest.
- Eleven core and security tables: `Users`, `Projects`, `Conversations`, `ToolPermissionGrants`, `AIActions`, `ActionApprovals`, `ActionExecutionStates`, `SecurityAuditEvents`, `SecurityAuditOutbox`, `ExecutionReconciliationAssessments`, and `ExecutionVerificationAssessments`.
- Selected critical columns, PostgreSQL types, nullability and length from the current EF configurations and five targeted migrations.
- Primary keys for all eleven tables; critical ownership and security foreign keys with their referenced table and delete action; unique indexes on `Users.AuthUserId`, active exact grants, execution approval/execution IDs and outbox audit-event IDs.
- Named, validated security check constraints, including grant status, execution state, enum bounds, reconciliation/verification state and outbox delivery. The hardened outbox delivery expression is compared with the Step 13J expression after presentation-only whitespace and parenthesis normalization.

Named check constraints other than the hardened delivery rule are checked for validation and required column markers; this is **not** full SQL-expression equivalence. Index definitions are checked for uniqueness, expected columns and selected predicates. A full physical schema audit, row-level data audit, exact core-schema baseline and migration provenance review remain separate work.

Failures are reported only as bounded categories: `DB CONNECTIVITY`, `MIGRATION HISTORY`, `MIGRATION BASELINE`, `CORE TABLES`, `KEY CONSTRAINTS`, `SECURITY SCHEMA`, `INPUT`, and `RESULT`. Internal stages distinguish missing history, missing/duplicate/unexpected/out-of-order IDs, missing table or column, incompatible column, missing key/FK/unique index/check and incompatible check. Raw database errors, connection strings, passwords and row values are never printed. A PASS reports the six categories and `RESULT: PASS`; any failure ends in `RESULT: FAIL`.

No migrations, fixture writes, cleanup, schema changes, background workers, retries or repair commands are part of this harness. A read-only DB role independently enforces that boundary.

## Manual PowerShell execution

Use a new PowerShell session from the repository root after the offline build. Confirm the database name, host, role and disposable status out of band. The role should have catalog and history SELECT access only. Do not use a production database. No password or connection string is written to a file.

```powershell
Add-Type -Path (Join-Path (Get-Location) 'security-checks/bin/Debug/net10.0/Npgsql.dll')
$securePassword = Read-Host 'Disposable DB read-only password' -AsSecureString
$passwordBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $builder = [Npgsql.NpgsqlConnectionStringBuilder]::new()
    $builder.Host = Read-Host 'Disposable DB host'
    $builder.Database = Read-Host 'Disposable DB name (aura_disposable_...)'
    $builder.Username = Read-Host 'Read-only DB user'
    $builder.Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordBstr)
    $builder.Timeout = 5
    $env:ConnectionStrings__DefaultConnection = $builder.ConnectionString
    $env:AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE = 'true'
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-migration-baseline-evidence
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection',
        'AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

Configure TLS as required by the known disposable host; do not weaken transport validation to obtain a PASS. The password and connection string necessarily exist in process memory during execution. Clearing the environment and zeroing the BSTR does not guarantee erasure of managed string copies.

## Validation and remaining blockers

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```

The offline suite uses synthetic catalog snapshots and a fake live runner; it performs no DB work. Even a reviewed live PASS would only establish the checked disposable database's observed state. It does not repair or approve the missing historical core migration baseline, establish application-order provenance, mark any composition dimension ready, enable tools or satisfy Step 14P final activation. Supabase identity evidence, real ownership isolation, permissions, concurrency, audit delivery, independent verification and production DI/final approval remain separate blockers.
