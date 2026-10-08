# Step 12F — Controlled live ownership verification harness

Step 12F adds an explicit live mode to the existing `security-checks` executable. Its ordinary run remains an offline, deterministic security suite. The live mode is isolated from normal `Aura.Api` startup and supplies evidence only; it does not activate production ownership authorization or change runtime readiness.

## Live procedure

Run the live mode only when a human has selected an existing, known-good Project conversation and its owner, and has supplied the following environment variables outside the repository:

| Variable | Required value |
| --- | --- |
| `AURA_LIVE_OWNERSHIP_VERIFY` | Exactly `true` |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string; use a read-only database account |
| `AURA_VERIFY_AUTH_USER_ID` | External `AuthUserId`, nonempty GUID |
| `AURA_VERIFY_INTERNAL_USER_ID` | Expected internal `User.Id`, nonempty GUID |
| `AURA_VERIFY_CONVERSATION_ID` | Existing Project conversation ID, nonempty GUID |
| `AURA_VERIFY_PROJECT_ID` | That conversation's project ID, nonempty GUID |

With those variables set in the process environment, run:

```powershell
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -- --live-ownership
```

Do not put credentials or tokens in the command line, committed configuration, or output. No JWT token is accepted by this harness. Without the exact opt-in value, live mode prints `LIVE OWNERSHIP VERIFY: SKIPPED`, exits zero, and does not read the connection string or connect to the database. Missing or malformed required input prints `LIVE OWNERSHIP VERIFY: INVALID INPUT` and exits nonzero before connecting.

The live path constructs the existing `AuraDbContext` with Npgsql and pgvector configuration and no tracking, checks connectivity, and invokes the reviewed `AuthenticatedUserMappingSource` and `OwnershipPersistenceSource`. Mapping must return `Found` with the exact external `AuthUserId` and expected internal `User.Id`; missing, inactive, ambiguous, or mismatched mapping fails. Ownership must return an exact binding for the internal user, conversation, and project, including an active user, owned Project conversation, its exact project relation, and owned project. Random foreign user, conversation, and project IDs must each be denied. Finally, a controlled current-user context containing the mapped internal ID calls `ExactOwnershipScopeValidator` with the exact `ActionScope` and the real ownership source. This tests EF SQL translation and query behavior against the configured PostgreSQL database when actually run. It does not verify an HTTP request or a real JWT.

The harness contains no `SaveChanges`, data insertion, update, deletion, fixture creation, migration, `EnsureCreated`, or `EnsureDeleted` path. All application data access is query-only; use a database credential restricted to read-only access for server-enforced protection. The output consists only of fixed category lines, never IDs, connection strings, passwords, JWTs, SQL, or raw exceptions. A successful run prints `DB CONNECTIVITY`, `AUTH USER MAPPING`, `OWNERSHIP LOOKUP`, `OWNERSHIP NEGATIVE CHECKS`, and `COMPOSITION` as `PASS`, then `RESULT: PASS`, and exits zero. A failure prints only a fixed failing category or `RESULT: FAIL` and exits nonzero. `SKIPPED` means no live evidence was collected; `PASS` describes only this bounded verification, not production readiness.

## Runtime boundary and remaining work

Normal `Aura.Api/Program.cs` still does not call `AddOwnershipRuntimeFoundation`. Startup uses `UnavailableRuntimeCapabilityReadiness`; ownership readiness remains false, and the defaults for `ProductionAdaptersEnabled` and `ToolExecutionEnabled` remain false. Tool execution and Safe File Access remain disabled. This step adds no real tool handler, provider call, package, schema, migration, or production database write. It does not call OpenAI or Ollama.

Live PostgreSQL mapping and ownership queries, connectivity, and EF translation remain **unverified until an opted-in run against a real configured database passes**. Live Supabase/JWT authentication remains unverified and is deferred separately; this controlled current-user seam is not a substitute. Production authorization requires that live evidence, a reviewed JWT path, explicit runtime registration and readiness decisions, and the other production permission, execution-state, and audit requirements. A passing harness does not make production authorization or tool execution ready.

The .NET 10.0.401 solution build passed. The full offline security suite passed 1,208 checks, including package-free Step 12F opt-in, input, safe-output, query-only, exact-mapping, exact-ownership, negative-lookup, and composition checks. `git diff --check` passed. The live harness was **not run** in this step because no live opt-in or configured values were supplied.
