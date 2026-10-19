# Step 17Q — independent verification outcome migration and persistence review

## Result and migration review

**OPERATOR-REPORTED LIVE RESULT: PASS for migration and seven-ID baseline.** On the previously verified disposable `aura_disposable_test` target (database OID `24576`, system identifier `7696813047573034172`), the operator supplied `INDEPENDENT VERIFICATION MIGRATION APPLY: PASS`, followed by `DB CONNECTIVITY: PASS`, `MIGRATION HISTORY: PASS`, `MIGRATION BASELINE: PASS`, `CORE TABLES: PASS`, `KEY CONSTRAINTS: PASS`, `SECURITY SCHEMA: PASS`, and `RESULT: PASS`. Credentials and process environment variables were reported cleared after the run. This Codex process did not connect to PostgreSQL or apply the migration. The live independent-verification writer path remains **INCOMPLETE**.

`20261019000000_AddIndependentVerificationOutcome` drops and re-adds only `CK_ExecutionVerificationAssessments_State`. It adds no column, table, index, seed, data rewrite or broad SQL. The new constraint retains the historical Failed/Inconclusive branch, including null `ExecutionVersion` with None/0 and terminal version 2 with None/0 or OwnedProjectMetadata/1. Its separate Verified branch requires `ExecutionStatus=Succeeded (2)`, `Outcome=Verified (0)`, `ExecutionVersion=2`, `ProofSource=OwnedProjectMetadata (1)`, and `ProofVersion=1`. There is no default Verified. The EF configuration carries the same expression. Down restores the six-ID constraint. Down after any Verified row exists would fail unless those rows receive a separately reviewed retention or downgrade decision; do not delete evidence to force rollback.

The read-only Step 15B verifier now requires exactly seven ordered migration IDs, the last being `20261019000000_AddIndependentVerificationOutcome`. It keeps prior core-schema, ownership, key, column and audit checks. It requires the bounded Verified clause and legacy advisory markers in the validated verification check. The offline source-operation comparison checks the exact migration expression; catalog text markers are bounded shape evidence, not a SQL-equivalence proof or historical core-schema provenance. The supplied same-target seven-ID PASS establishes this bounded live Verified-constraint shape for the disposable target. It does not prove that a Verified assessment has been written.

## Controlled operator application

The new command `--apply-independent-verification-outcome-migration` is separate from API startup and the offline suite. It requires `AURA_APPLY_INDEPENDENT_VERIFICATION_OUTCOME_MIGRATION=true`, `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true`, `ConnectionStrings__DefaultConnection`, positive `AURA_EXPECTED_DATABASE_OID` and `AURA_EXPECTED_SYSTEM_IDENTIFIER`. It checks live database identity, exactly the first six applied migrations and exactly one pending migration, the seventh, before invoking EF's targeted migrator. It suppresses raw exceptions. Ambiguous failure requires read-only history and catalog inspection before any further action; do not blindly rerun.

Normal or remote reviewed targets require `SSL Mode=VerifyFull`. The only non-TLS path is the independently verified disposable loopback `127.0.0.1:5433` (or exact `::1` when independently verified), database `aura_disposable_test`, OID `24576`, system identifier `7696813047573034172`, with `AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE=true`. The command rejects `localhost`, hostnames or remote non-TLS, another port/database/identity, missing gate and unexpected history. This exception exists only in explicit security-checks migration evidence tooling; Aura.Api production startup transport and readiness are unchanged.

From the repository root in a fresh PowerShell process, the verified local disposable operator can use this procedure. Confirm target and migration role against the approved inventory before entering credentials. Capture only bounded status lines and non-secret identity values.

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
    $env:AURA_APPLY_INDEPENDENT_VERIFICATION_OUTCOME_MIGRATION = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --apply-independent-verification-outcome-migration
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
        'AURA_APPLY_INDEPENDENT_VERIFICATION_OUTCOME_MIGRATION',
        'AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

For a separately reviewed TLS target, use the same exact application and baseline commands with `SslMode=VerifyFull` and leave `AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE` unset. Never downgrade a remote target to Disable. A successful application must be followed by the separate read-only baseline lines: `DB CONNECTIVITY`, `MIGRATION HISTORY`, `MIGRATION BASELINE`, `CORE TABLES`, `KEY CONSTRAINTS`, `SECURITY SCHEMA` and `RESULT: PASS`. The operator supplied those bounded lines for the verified disposable target; the procedure remains for future reviewed runs.

## Writer, repeated assessments and live verification

`ExecutionVerificationAssessmentPersistenceWriter` remains advisory-only after the live schema PASS. It accepts only Failed/Inconclusive and stores None/0 proof. `VerificationEvidence` carries outcome and IDs but does **not** carry the independently observed `OwnedProjectMetadataObservation`, proof source/version or execution version. The writer accepts `executionVersion` as a separate caller argument. `ExecutionEvidenceInput.ProjectObservation` is a publicly constructible input to the workflow; copying its fields without preserving a reviewed trusted-source boundary would not establish provenance. Extending the writer merely because the constraint now permits Verified would allow a policy outcome or handler report to be mistaken for independent evidence. It therefore remains unregistered. The next contract change is a server-issued, immutable proof-bearing `VerificationEvidence` that binds the exact observed project and persisted timestamp, source/version, terminal execution version, and all action/user/conversation/project/tool/approval/execution IDs. Issuance must validate the trusted independent source and successful durable state; the writer must recheck those exact bindings and authoritative observation before a Verified insert. This requires separate offline and live persistence evidence.

The table uses distinct assessment IDs with no execution-unique constraint. Repeated assessments are historical observations, not replacements or an implicit current/latest verdict; no uniqueness rule is added. Each future row must independently bind to the exact durable execution and proof. No live independent-verification harness was added because the Verified writer path is incomplete. There is no `--live-production-independent-verification-evidence` PASS to report. A future disposable-fixture harness must exercise every wrong binding, failed execution, missing proof, immutable execution state, repeated rows and cleanup; until then its result is **INCOMPLETE**.

## DI, readiness and validation

The intended scoped graph remains source → `OwnedProjectMetadataExecutionEvidenceSource`, writer → `ExecutionVerificationAssessmentPersistenceWriter`, and verification workflow → `VerificationWorkflowService`, with a compatible `IDbContextFactory<AuraDbContext>`. No production source, writer, workflow, factory or reviewed catalog entry was registered. The seven-ID database PASS closes the schema portion only. `Verification=false`, `ToolHandlers=false`, `ExecutionPolicy=false`, `TrustedExecution=false`, `CompositionComplete=false`; all runtime flags remain false, final approval remains unavailable and activation denied. Historical core-schema provenance and other ownership/approval-chain blockers remain separate.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
git status --short
git diff --stat
```
