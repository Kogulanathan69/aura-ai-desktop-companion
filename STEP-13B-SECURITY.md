# Step 13B — Exact permission adapter and controlled PostgreSQL verification

`ToolPermissionPersistenceSource` is an unregistered, read-only Infrastructure implementation of `IToolPermissionSource`. It reads only the dedicated `ToolPermissionGrants` table from Step 13A. Generic `Permissions` rows used by project-file access are never interpreted as tool grants. No grant issuance, revocation write, automatic migration, fixture insertion, runtime registration, or readiness change is part of this step.

## Exact lookup and scope integrity

Each valid request requires nonempty exact internal user, conversation and project IDs, an authoritative `ToolIdentifier`, and a defined `ToolPermissionRequirement`. A single bounded, no-tracking query filters exact `UserId`, `ConversationId`, `ProjectId`, `ToolId`, numeric `RequiredPermission`, `Status=Active`, and `RevokedAt IS NULL`. In that same SQL statement it requires an active internal user, a `Project`-type conversation owned by that user and linked to that exact project, and a project owned by that user. The grant row and independent FKs alone are not ownership proof; `ExactOwnershipScopeValidator` remains a separate execution gate.

The query projects only bounded scalar fields, takes at most two rows, and never returns the EF entity. Zero qualifying rows return `NotFound`. Two rows return `Unavailable`; it never silently selects a first row even though the Step 13A partial unique index should prevent active duplicates. The projected row is checked again for nonempty ID, exact scope, active/non-revoked state, and canonical tool ID. `ToolIdentifier.TryCreate` reparses persisted `ToolId`; noncanonical or malformed values cannot authorize. Numeric permission values are explicitly mapped from 0, 1, or 2 to `OwnedProjectRead`, `OwnedProjectWrite`, or `ExternalAction`; unknown values cannot authorize. There is no wildcard, permission hierarchy, role or owner inference, approval bypass, fallback, or retry.

Missing, revoked, inactive, foreign, or malformed matching data denies. Database errors produce bounded `Unavailable` with no raw exception details. Cancellation propagates. All reads use `AsNoTracking`; the source contains no `SaveChanges`, insert, update, delete, or grant lifecycle path. This is an adapter foundation, not production authorization: EF translation and PostgreSQL behavior remain unverified until a controlled live run passes.

The lookup is one consistent query snapshot, not a lock held through tool execution. A grant could be revoked after this read and before a future handler invocation. Step 13C and the production execution review must define the revocation/authorization timing guarantee, including whether the durable reservation transaction must revalidate or lock the grant. This source alone cannot justify production execution.

## Migration baseline and live procedure

The repository contains the targeted Step 13A `20261014000000_AddToolPermissionGrants` migration but no preceding EF model snapshot/baseline. Existing target database schema and `__EFMigrationsHistory` cannot be assumed. This step does **not** apply a migration. A human must review the target's existing base schema and migration history before any manual migration application or later EF scaffolding. The live verifier requires the exact migration ID in history and checks that the grant table, expected columns and named constraints, and a partial unique index are present. That check is a bounded schema-shape check, not proof of full database security or a substitute for migration review.

Use a read-only PostgreSQL credential and a known existing test grant. Supply these values as process environment variables, never in committed files or command arguments:

| Variable | Requirement |
| --- | --- |
| `AURA_LIVE_PERMISSION_VERIFY` | Exactly `true` |
| `ConnectionStrings__DefaultConnection` | Existing PostgreSQL connection string |
| `AURA_VERIFY_PERMISSION_USER_ID` | Nonempty internal `Users.Id` GUID |
| `AURA_VERIFY_PERMISSION_CONVERSATION_ID` | Existing Project conversation GUID |
| `AURA_VERIFY_PERMISSION_PROJECT_ID` | Existing owned project GUID |
| `AURA_VERIFY_PERMISSION_TOOL_ID` | Exact canonical tool ID |
| `AURA_VERIFY_PERMISSION_REQUIREMENT` | One of `OwnedProjectRead`, `OwnedProjectWrite`, `ExternalAction` |
| `AURA_VERIFY_PERMISSION_GRANT_ID` | Optional nonempty expected grant GUID |

Then run `dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -- --live-permission`. If the command environment needs the single-worker fallback, add `-p:BuildInParallel=false -p:UseSharedCompilation=false` before `--`.

Without the exact opt-in, live mode prints `LIVE PERMISSION VERIFY: SKIPPED`, exits zero, and reads no other input or database. Missing or malformed input prints `LIVE PERMISSION VERIFY: INVALID INPUT` and exits nonzero before the runner. A live attempt first checks connectivity, then catalog/schema and migration history. It invokes the real source for the known positive grant and, on success, checks mutated user, conversation, project, tool, and requirement requests. No known grant prints `PERMISSION LOOKUP: NO TEST GRANT` and fails; it is never a passing result. Passing output contains only `DB CONNECTIVITY`, `SCHEMA CHECK`, `PERMISSION LOOKUP`, `NEGATIVE CHECKS`, and `RESULT` fixed categories. Failures are fixed category lines; no connection string, password, raw SQL, grant contents, token, ID, or exception is printed. The verifier never inserts fixtures or calls migration APIs.

The ordinary security suite remains offline and does not invoke live mode. A `SKIPPED` result supplies no live evidence. A `PASS` would verify only the selected database, schema-shape checks and existing grant behavior at that time; it would not enable production permission readiness, prove JWT behavior, or justify tool execution.

## Runtime and remaining work

Normal `Aura.Api/Program.cs` does not register `ToolPermissionPersistenceSource` or any production `IToolPermissionSource`. Both startup and informational Permission readiness remain false. `ProductionAdaptersEnabled`, `ToolExecutionEnabled`, and `SafeFileAccessEnabled` default false. No real handler, Safe File Access, or OpenAI/Ollama call is activated.

Step 13C requires target-database baseline and migration review, an explicitly opted-in live PostgreSQL run with a known test grant, exact SQL/constraint and concurrency review, grant issuance/revocation lifecycle design and authorization, then a separate runtime composition and readiness decision. The independent ownership and approval gates, execution policy, durable reservation, audit, verification and reconciliation blockers also remain.

Package-free Step 13B checks cover exact lookup, scope integrity, inactive/revoked/foreign/malformed denials, duplicate detection, bounded read-only behavior, error and cancellation handling, opt-in and safe output, and inactive runtime registration. These offline checks do not establish live PostgreSQL behavior.

With .NET SDK 10.0.302, the requested default solution build exited 1 with zero reported warnings or errors in this command environment. The prescribed single-worker fallback passed with zero warnings and errors. The plain security-check command hit the same build anomaly; the fallback passed **1,438 offline checks**. The live permission mode was **not run** because no explicit opt-in and known test grant were supplied. No PostgreSQL migration or schema verification was performed live. `git diff --check` passed.
