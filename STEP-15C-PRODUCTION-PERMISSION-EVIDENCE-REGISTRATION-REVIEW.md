# Step 15C — production permission evidence and registration review

## Purpose and current verdict

This step extends the existing read-only `LivePermissionVerification` harness with an explicit production-evidence command. It reviews the exact `ToolPermissionGrants` boundary and the registration conditions for future production use. **No live PostgreSQL command was run in this step. Permission composition remains false; full composition remains NOT READY.** An offline PASS or harness file is not live evidence.

## Existing permission architecture

`ToolPermissionPersistenceSource` is the production-capable, read-only `IToolPermissionSource` implementation. Normal `Aura.Api/Program.cs` does **not** register it. It requires a unique scoped `IAuraDbContext` when registered in the future; the source itself must have one scoped concrete registration. There is no current production lifetime to claim.

The application request binds the exact internal `UserId`, `ConversationId`, `ProjectId`, canonical `ToolId`, and `ToolPermissionRequirement` (`OwnedProjectRead`, `OwnedProjectWrite`, or `ExternalAction`). The source queries `ToolPermissionGrants` with all five values, requires `Status == Active` and `RevokedAt == null`, and also requires an active user, an owned Project-type conversation linked to the exact project, and a project owned by that user. It uses no tracking, takes at most two rows, rejects ambiguous results, and returns `NotFound` for absent or mismatched grants. Invalid stored grant details or a DB failure yield bounded `Unavailable`; neither authorizes use. The separate `ExactToolPermissionPolicy` requires the returned grant's exact scope, tool, requirement, active status and non-revoked state. `ToolExecutionPermissionValidator` queries the source on each invocation. Ownership, approval, durable execution state and an allowing execution policy remain independent gates.

Generic `Permissions` rows, roles, ownership alone and approvals are not tool grants. The five-part exact binding is never inferred from them. Grant issuance, revocation authorization and revocation timing before handler invocation still require separate review.

## Live command and environment gates

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-permission-evidence
```

Both `AURA_LIVE_PRODUCTION_PERMISSION_EVIDENCE=true` and `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true` must be present, exactly lowercase `true`. If either is absent, the command prints `PRODUCTION PERMISSION EVIDENCE: SKIPPED` without reading the connection string, creating a DbContext or accessing PostgreSQL. SKIPPED is not evidence. The older `--live-permission` command and its gate remain unchanged.

| Environment variable | Meaning |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Known disposable PostgreSQL database, using a read-only role. Its name must begin `aura_disposable_` with a nonempty suffix. |
| `AURA_VERIFY_PERMISSION_USER_ID` | Internal owner GUID of the known active grant. |
| `AURA_VERIFY_PERMISSION_CONVERSATION_ID` | Exact owned Project conversation GUID. |
| `AURA_VERIFY_PERMISSION_PROJECT_ID` | Exact owned project GUID. |
| `AURA_VERIFY_PERMISSION_TOOL_ID` | Canonical lowercase tool ID; no trim or case normalization is accepted for live input. |
| `AURA_VERIFY_PERMISSION_REQUIREMENT` | Exact enum name: `OwnedProjectRead`, `OwnedProjectWrite`, or `ExternalAction`. |
| `AURA_VERIFY_PERMISSION_GRANT_ID` | Optional expected active grant GUID; if supplied it must match the result. |
| `AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID` | Optional existing revoked grant GUID. Required for a full `RESULT: PASS`. |

GUIDs, canonical tool ID, permission name and connection-string structure are validated before DB access. The database prefix and acknowledgement are safety gates, not proof that a DB is genuinely disposable. Verify host, database and role out of band. Step 15B's broader migration/schema verifier is a separate command; its presence is not a reviewed live baseline PASS. This command reuses the existing narrow permission-schema probe and does not repeat the broad catalog audit.

## Positive and negative evidence

The live verifier uses the actual `ToolPermissionPersistenceSource` to look up the exact active grant. It checks the returned grant ID (and optional expected ID), full scope, tool, requirement, active state and non-revocation. It then checks wrong UserId, ConversationId, ProjectId, ToolId and requirement. A randomly generated wrong expected grant ID must fail the harness's exact-result assertion; grant ID is **not** a substitute for the five-part source query. Random negative IDs are useful fail-closed checks, but they do not replace separately reviewed adversarial fixtures containing real other users' rows.

If `AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID` is supplied, the verifier reads that row without changing it. It requires a genuinely revoked record with `RevokedAt` set, canonical stored tool and defined requirement, plus an active owner, owned Project conversation and owned project. It queries the source using the revoked row's own exact five-part binding and requires `NotFound`. The fixture must not have a simultaneously active grant for that same binding. A missing or invalid supplied fixture fails. If no revoked ID is supplied, the command reports `REVOKED GRANT CHECK: NOT PROVEN` and `RESULT: INCOMPLETE` with nonzero exit status; it cannot report overall PASS. The inactive-user denial is covered by the existing offline source tests, but a real inactive-user permission fixture is not separately claimed by this live command.

Successful output is limited to `DB CONNECTIVITY`, `PERMISSION SCHEMA`, `EXACT GRANT LOOKUP`, `SCOPE NEGATIVE CASES`, `TOOL/PERMISSION NEGATIVE CASES`, `REVOKED GRANT CHECK`, and `RESULT: PASS`. Failures and incomplete evidence use fixed category labels only. The harness never prints connection strings, passwords, raw exceptions, JWTs, IDs or arbitrary row contents.

The harness issues only no-tracking reads. It performs no grant creation, mutation, revocation, cleanup, schema change or migration. Use a read-only DB role as an independent guard.

## Manual PowerShell run

Run from the repository root in a fresh PowerShell session after confirming a known disposable DB with pre-existing active and revoked fixtures. The role should have SELECT access and no write privileges. No secret is saved to a file.

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
    $env:AURA_VERIFY_PERMISSION_USER_ID = Read-Host 'Internal UserId GUID'
    $env:AURA_VERIFY_PERMISSION_CONVERSATION_ID = Read-Host 'Owned ConversationId GUID'
    $env:AURA_VERIFY_PERMISSION_PROJECT_ID = Read-Host 'Owned ProjectId GUID'
    $env:AURA_VERIFY_PERMISSION_TOOL_ID = Read-Host 'Canonical ToolId'
    $env:AURA_VERIFY_PERMISSION_REQUIREMENT = Read-Host 'Permission enum name'
    $env:AURA_VERIFY_PERMISSION_GRANT_ID = Read-Host 'Expected active GrantId GUID (optional)'
    $env:AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID = Read-Host 'Existing revoked GrantId GUID (required for full PASS)'
    $env:AURA_LIVE_PRODUCTION_PERMISSION_EVIDENCE = 'true'
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-permission-evidence
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection',
        'AURA_VERIFY_PERMISSION_USER_ID', 'AURA_VERIFY_PERMISSION_CONVERSATION_ID',
        'AURA_VERIFY_PERMISSION_PROJECT_ID', 'AURA_VERIFY_PERMISSION_TOOL_ID',
        'AURA_VERIFY_PERMISSION_REQUIREMENT', 'AURA_VERIFY_PERMISSION_GRANT_ID',
        'AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID',
        'AURA_LIVE_PRODUCTION_PERMISSION_EVIDENCE',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

Configure TLS for the known disposable host without weakening validation. The password and connection string exist in process memory during execution; clearing environment variables and zeroing the BSTR cannot guarantee erasure of managed string copies.

## Production registration review and remaining blockers

`ProductionRuntimeCompositionInspector` requires exactly one `IToolPermissionSource` registration, scoped lifetime, concrete `ToolPermissionPersistenceSource` implementation type, and a reviewed entry in its production catalog. The catalog is empty and normal API startup has no such registration. Duplicate, factory, instance, singleton, transient and unavailable-source registrations cannot satisfy this rule. A future production change must explicitly review the source, its scoped `IAuraDbContext` dependency, the physical grant schema, live evidence, grant issuance/revocation policy and timing, and intentional normal-startup registration before changing the catalog. This step changes neither DI nor the catalog. Even a reviewed live PASS cannot itself set Permission true or satisfy the other Step 14P composition and final security gates.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```

Runtime PostgreSQL permission evidence, separate real Supabase identity and ownership evidence, migration baseline review, authorized grant lifecycle, revocation race handling, production DI/catalog review, and final activation approval remain blockers. Production tool execution, Safe File Access and Cloud AI remain disabled.
