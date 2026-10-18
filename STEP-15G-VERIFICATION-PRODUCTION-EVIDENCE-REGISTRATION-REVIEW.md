# Step 15G — verification production evidence and registration review

## Purpose, scope and verdict

This step adds an explicit disposable-PostgreSQL command for the current verification assessment schema and conservative policy. It reuses generated execution fixtures and exact cleanup helpers. It does not create an independent proof adapter, register verification in the API, add a production assessment writer, enable execution, run a tool/provider or change schema. **Verification: false. CompositionComplete: false — NOT READY.** Harness existence and offline PASS are not live PostgreSQL evidence. No live run is claimed here.

The truthful successful live result is **`RESULT: INCOMPLETE`**, with `INDEPENDENT PROOF ADAPTER: NOT PROVEN`. The command uses exit code 2 for this state. It never prints an overall PASS, because no independent production proof source exists.

## Workflow and independent-proof boundary

`VerificationWorkflowService` requires an approved action context, bound approval, authenticated ownership validator, known disabled tool descriptor, and a bounded `ExecutionEvidenceInput` from `IExecutionEvidenceSource`. It checks exact action, user/conversation/project scope, tool, approval and execution IDs; requires a UTC completion time after approval; calls `IIndependentVerificationPolicy`; and rechecks ownership before issuing advisory `VerificationEvidence`. It neither runs a tool nor mutates durable execution state or writes an assessment row.

`UnavailableExecutionEvidenceSource` is the only concrete `IExecutionEvidenceSource` in production assemblies. It returns null. No independent production evidence adapter is implemented or registered. `ConservativeVerificationPolicy` maps a Failed execution report to Failed and Succeeded/Unknown to Inconclusive. A success report is **not proof** and cannot become Verified under that policy. The workflow also rejects Verified if the execution report is Failed or Unknown, but a future Verified policy must independently establish tool-specific proof. Existing synthetic offline tests exercising a fake proof policy are not production evidence.

`ExecutionEvidenceInput` carries execution ID, action, scope, tool, approval, report outcome and completion time. It carries **no durable execution version, proof source, proof version or independently attested provenance**. The workflow can compare the available IDs and timestamps but cannot prove exact version binding or reject a proof made stale after a state transition. Those are production blockers, not properties silently supplied by the assessment table.

## Assessment model and outcome limits

`ExecutionVerificationAssessments` is a schema foundation, not a production writer. Its row has assessment ID, action/user/conversation/project/tool/approval/execution IDs, terminal execution status, outcome, proof source/version and assessment time. The current `VerificationProofSource` enum contains only `None=0`. `ProofVersion=0` is the only permitted version. The named state check constraint accepts only Succeeded or Failed execution status, Failed or Inconclusive verification outcome, ProofSource None and ProofVersion 0. It rejects a persisted Verified claim, unknown source and nonzero version. The table has individual foreign keys and an assessment-ID primary key; it has no composite binding constraint for every scope column, no durable execution-version column, and no action/execution uniqueness constraint. Repeated advisory rows are therefore permitted by schema. No production assessment persistence adapter exists.

The live harness uses generated terminal Succeeded and Failed execution fixtures. It directly inserts test-only Inconclusive and Failed assessment rows through EF, reads back every stored field, inserts a second Inconclusive assessment for the same execution, and verifies the durable states remain unchanged. It checks PostgreSQL rejects an attempted Verified row, unknown proof source and nonzero proof version under the named constraint. This validates the database's current bounded shape and repeat behavior; it **does not** prove a production verification workflow writes those rows. The harness does not fabricate an independent proof or claim that a success report was verified.

## Live command, gates and output

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-verification-evidence
```

Required process environment:

- `AURA_LIVE_PRODUCTION_VERIFICATION_EVIDENCE=true`
- `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true`
- `ConnectionStrings__DefaultConnection` with host, username and database named `aura_disposable_...`

The acknowledgement values must be exactly lowercase `true`. If either is absent, the command prints `PRODUCTION VERIFICATION EVIDENCE: SKIPPED` and returns before reading the connection, creating a context or contacting PostgreSQL. SKIPPED is not evidence. A malformed or non-prefixed connection fails before the DB runner. The prefix and acknowledgement do not prove a DB is disposable; the operator must independently verify its host, identity, write role, authorization and disposal policy.

The DB needs the reviewed Step 15B baseline, core fixture dependencies, durable execution state, verification assessment and outbox tables. The harness checks relevant table and migration presence without duplicating broad schema validation. It never applies migrations or creates schema. It writes only generated fixtures and assessment rows. Cleanup deletes exact generated assessment IDs first, then exact generated event/outbox/action-state/approval/action/conversation/project/user IDs; it verifies generated rows are absent. Cleanup failure returns FAIL. A crash or lost connection may leave fixtures, so use only a verified disposable DB. The harness does not snapshot unrelated rows; its deletes are ID-scoped rather than broad.

An observed database run prints PASS for DB CONNECTIVITY, SCHEMA CHECK, FIXTURE SETUP, ASSESSMENT PERSISTENCE, PROOF SOURCE RULES, CONSERVATIVE OUTCOME, REPEATED ASSESSMENT, STATE IMMUTABILITY and CLEANUP. It then prints `NEGATIVE BINDINGS: PARTIAL`, `EXECUTION VERSION BINDING: NOT PROVEN`, `INDEPENDENT PROOF ADAPTER: NOT PROVEN`, `RESULT: INCOMPLETE`. Individual foreign keys and the workflow's synthetic binding checks do not prove all valid-but-cross-linked database IDs are rejected, and there is no real proof source to exercise. A failing run prints a bounded category and `RESULT: FAIL`. No connection string, password, raw exception, arbitrary row/proof payload or stack trace is printed.

## Manual PowerShell operation

Use a fresh PowerShell session at the repository root after verifying the disposable DB and Step 15B baseline out of band. Use a role scoped to generated-fixture writes and catalog/history reads. Configure TLS as required without weakening certificate validation. Do not save credentials to a file.

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
    $env:AURA_LIVE_PRODUCTION_VERIFICATION_EVIDENCE = 'true'
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-verification-evidence
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection',
        'AURA_LIVE_PRODUCTION_VERIFICATION_EVIDENCE',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

Password and connection strings necessarily exist in process memory during the run. Clearing environment and BSTR does not guarantee erasure of managed string copies. Capture the command, time, verified disposable DB identity, sanitized output and cleanup result in the approved record without credentials or proof/row contents.

## Production registration review and remaining blockers

Normal `Aura.Api/Program.cs` registers no `IExecutionEvidenceSource` and retains `UnavailableRuntimeCapabilityReadiness`. `ProductionRuntimeCompositionInspector` requires exactly one scoped `IExecutionEvidenceSource` implementation other than `UnavailableExecutionEvidenceSource`, independently present in `ReviewedVerification`. The catalog is empty because no eligible production implementation exists. Duplicate, factory, instance, wrong-lifetime, unavailable or unreviewed sources cannot assert readiness. A live DB `INCOMPLETE` result changes neither DI nor catalog.

Before Verification can be ready, implement and review a genuinely independent, scope-bound proof source and policy; define proof source/version provenance and exact durable-state version/freshness; reject stale, mismatched and missing evidence; decide and implement production assessment persistence and repeated-assessment/idempotency behavior; review composite binding integrity and live results; and deliberately review normal-startup DI and catalog membership. Ownership, Permission, ExecutionState, AuditSink, AuditDelivery, Reconciliation, ExecutionPolicy, ToolHandlers, TrustedExecution and Step 14P final activation remain independent blockers. All ten composition fields and CompositionComplete remain false. Production execution, Safe File Access and Cloud AI stay disabled.

## Offline validation

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```

The normal suite uses an injected offline runner, conservative-policy checks and EF metadata. It does not connect to PostgreSQL.
