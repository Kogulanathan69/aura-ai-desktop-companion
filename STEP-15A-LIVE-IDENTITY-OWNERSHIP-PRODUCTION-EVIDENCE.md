# Step 15A — live identity and ownership production evidence

## Purpose and current verdict

This step extends the existing read-only Step 12F ownership harness with an explicit `--live-production-ownership-evidence` command. It collects controlled PostgreSQL evidence for the external subject → active local user → exact project → exact project conversation → scope validator path. **No live run was performed in this step. A harness and offline PASS are not live PASS evidence.** Ownership production composition and final activation remain **NOT READY**.

## Reviewed identity and ownership chain

The API's JWT bearer configuration disables inbound claim mapping. `CurrentUserService` and the future, unregistered `HttpCurrentUserContext` require one authenticated identity and its one raw `sub` claim. The subject is parsed as a nonempty GUID and passed as `Users.AuthUserId` to `AuthenticatedUserMappingSource`. That source makes a bounded, no-tracking lookup requiring `Users.IsActive`; zero, ambiguous and unavailable results fail closed. EF config declares a unique index on `Users.AuthUserId`. A live query does not independently prove the physical index or validate a signed Supabase JWT.

`ProjectService` derives internal `Users.Id` from `UserIdentityService` and filters project operations by `Project.UserId`. `ConversationService` uses owned-conversation queries and checks ownership of any selected project. The workflow `OwnershipPersistenceSource` requires one active user, a Project-type conversation with exact `UserId` and `ProjectId`, and a project owned by that user. `ExactOwnershipScopeValidator` compares the authenticated internal user against the entire `ActionScope` binding. Supplied GUIDs are lookup values, never authority.

## Live command and gates

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-ownership-evidence
```

The command requires both `AURA_LIVE_PRODUCTION_OWNERSHIP_EVIDENCE=true` and `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true` (exact lowercase `true`). Missing either gate prints `PRODUCTION OWNERSHIP EVIDENCE: SKIPPED`, returns success for a deliberate skip, and does not read the connection string or create a context. Do not count SKIPPED as evidence. The legacy `--live-ownership` command retains its previous gate and behavior.

After both gates, these environment variables are required:

| Variable | Meaning |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Connection to a known disposable PostgreSQL database with preloaded, reviewed fixtures. Use a read-only database role. |
| `AURA_VERIFY_AUTH_USER_ID` | Raw Supabase subject / `Users.AuthUserId` GUID for the active fixture. |
| `AURA_VERIFY_INTERNAL_USER_ID` | Expected local `Users.Id` GUID. |
| `AURA_VERIFY_PROJECT_ID` | Project owned by that local user. |
| `AURA_VERIFY_CONVERSATION_ID` | Project-type conversation owned by that user and linked to that project. |
| `AURA_VERIFY_INACTIVE_AUTH_USER_ID` | Optional existing inactive `Users.AuthUserId` GUID. When supplied, the harness requires a real inactive row and confirms it cannot map. |

All supplied GUIDs are checked before reading the connection string or invoking the DB runner. The connection string must parse and contain a host and database. Use a disposable database only, even though this harness issues no writes. The harness does not create fixtures, apply migrations, change schema, delete rows or perform cleanup. The operator must verify fixture ownership and database identity out of band before running. A read-only database role provides an independent guard against accidental writes.

## Evidence scenarios and limits

The command checks connectivity and queries the configured Users, Projects and Conversations tables. It verifies exact active subject-to-local-user mapping; unknown and empty subject mapping failure; optional real inactive-user mapping failure; exact owned Project and Project-type Conversation relation; exact ownership source and scope validator acceptance; and rejection of changed user, project and conversation IDs. A wrong project also tests the conversation-to-project binding. These negative IDs are generated fresh, so they do **not** prove denial of an existing other user's row. For adversarial cross-user and cross-project fixture evidence, preload such rows and conduct a separately reviewed test before claiming complete production isolation. Schema probing confirms table access, not a safe migration baseline or every constraint.

The command does not receive, parse or validate a real Supabase JWT. It uses a supplied subject GUID and the actual mapping/ownership source implementations. Real JWT issuer, signature, audience, lifetime and `sub` handling still need separate runtime evidence. A positive result is evidence for this DB lookup chain only; it does not review production DI, populate the inspector's reviewed catalog, turn on runtime flags, or enable tool execution.

Output is limited to named `PASS`, `FAIL`, `SKIPPED`, `INPUT: FAIL` and `RESULT` lines. Raw exceptions, connection strings, passwords, tokens, claim values and row values are never printed. A successful result prints `DB CONNECTIVITY`, `SCHEMA CHECK`, `AUTH SUBJECT MAPPING`, `AUTH NEGATIVE CASES`, `PROJECT OWNERSHIP`, `CONVERSATION OWNERSHIP`, `SCOPE NEGATIVE CASES`, and `RESULT: PASS`. Failure prints a fixed failed category and `RESULT: FAIL`, or only `RESULT: FAIL` for an unexpected exception.

## Manual PowerShell run

Run from the repository root in a fresh PowerShell session after an operator confirms the DB is disposable and has the required fixtures. The read-only DB user should already have access to the existing schema. No password or connection string is saved to a file.

```powershell
Add-Type -Path (Join-Path (Get-Location) 'security-checks/bin/Debug/net10.0/Npgsql.dll')
$securePassword = Read-Host 'Disposable DB read-only password' -AsSecureString
$passwordBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $builder = [Npgsql.NpgsqlConnectionStringBuilder]::new()
    $builder.Host = Read-Host 'Disposable DB host'
    $builder.Database = Read-Host 'Disposable DB name'
    $builder.Username = Read-Host 'Read-only DB user'
    $builder.Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordBstr)
    $builder.Timeout = 5
    $env:ConnectionStrings__DefaultConnection = $builder.ConnectionString
    $env:AURA_VERIFY_AUTH_USER_ID = Read-Host 'Active AuthUserId (raw sub GUID)'
    $env:AURA_VERIFY_INTERNAL_USER_ID = Read-Host 'Expected local Users.Id GUID'
    $env:AURA_VERIFY_PROJECT_ID = Read-Host 'Owned Project.Id GUID'
    $env:AURA_VERIFY_CONVERSATION_ID = Read-Host 'Owned project Conversation.Id GUID'
    $env:AURA_VERIFY_INACTIVE_AUTH_USER_ID = Read-Host 'Inactive AuthUserId GUID (optional)'
    $env:AURA_LIVE_PRODUCTION_OWNERSHIP_EVIDENCE = 'true'
    $env:AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE = 'true'
    dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-ownership-evidence
} finally {
    foreach ($name in @('ConnectionStrings__DefaultConnection', 'AURA_VERIFY_AUTH_USER_ID',
        'AURA_VERIFY_INTERNAL_USER_ID', 'AURA_VERIFY_PROJECT_ID', 'AURA_VERIFY_CONVERSATION_ID',
        'AURA_VERIFY_INACTIVE_AUTH_USER_ID', 'AURA_LIVE_PRODUCTION_OWNERSHIP_EVIDENCE',
        'AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    if ($null -ne $builder) { $builder.Password = '' }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBstr)
    $securePassword.Dispose()
}
```

Use appropriate TLS configuration for the disposable DB, supplied by the operator. Do not copy a production connection string into this session. The environment variable and managed string exist in process memory during the run; cleanup removes the environment variable and zeroes the BSTR, but cannot guarantee erasure of every managed copy.

## Validation and remaining blockers

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```

Normal offline checks exercise gates, input validation, bounded output, fail-closed source behavior, and unchanged startup. They do not connect to PostgreSQL. Before Ownership can become ready, obtain reviewed live DB and Supabase token evidence, check physical schema and migration history, review adversarial fixtures, register the exact production ownership context/source/validator with correct DI lifetimes, resolve the inspector's factory-alias rule, and review all Step 14F catalog requirements. The other nine composition dimensions and final Step 13L approval remain separate blockers. Production execution, Safe File Access and Cloud AI remain disabled.
