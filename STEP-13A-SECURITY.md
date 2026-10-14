# Step 13A — Exact tool-permission schema foundation

The existing `Permissions` table supports project-file access through generic `ResourceType`, `ResourceIdentifier`, and `AccessLevel`. It lacks an exact required conversation, tool identifier, typed requirement, and required project binding. Those generic fields are unchanged and are not interpreted as tool-execution grants. Step 13A adds a separate `ToolPermissionGrants` model and a targeted additive migration. No grant is created, copied, seeded, queried for production authorization, or registered at runtime.

## Exact grant model

`ToolPermissionGrantRecord` maps to `ToolPermissionGrants` with required nonempty UUID `Id`, `UserId`, `ConversationId`, and `ProjectId`; required `ToolId`; constrained numeric `RequiredPermission`; numeric `Status`; optional `RevokedAt`; and required `CreatedAt` and `GrantedAt`. A database check rejects zero UUIDs. The tool ID is `varchar(64)` with a PostgreSQL check for the canonical `ToolIdentifier` form: a lowercase ASCII letter followed by up to 63 lowercase ASCII letters, digits, or hyphens. There is no wildcard, role, owner, approval, generic resource, or free-text grant field.

`RequiredPermission` is an integer constrained to 0, 1, or 2, corresponding to the current `ToolPermissionRequirement` values `OwnedProjectRead`, `OwnedProjectWrite`, and `ExternalAction`. The Domain model stores that constrained numeric representation without depending on the Application enum. A future adapter must explicitly map and reject unknown values; the schema cannot silently gain a new permission when the Application enum changes. Security checks pin the correspondence.

`Status` is the Domain enum `Active=1` or `Revoked=2`, persisted as an integer. A check requires `RevokedAt IS NULL` for active rows and non-null for revoked rows; no third or default-allow state is valid in the database. This step does not implement grant issuance or revocation writes. A future writer must define UTC timestamp, transition, and authorization rules. `GrantedAt` and `CreatedAt` are required but the current schema does not independently attest who issued a grant or enforce server-generated time, so mere row presence is not production permission evidence.

There are required FKs to `Users`, `Conversations`, and `Projects`, all with Restrict deletion. No required authorization dimension can be SetNull. There is no FK to ToolId because tools are not persisted entities. Independent FKs do not prove that the conversation and project are owned by the same user or associated with each other. The existing separate ownership gate and a future exact-match adapter must check that relationship. Restrict may block deletion of a referenced user, conversation, or project until grant lifecycle is explicitly handled; it cannot silently create a partial grant.

A partial unique index over `(UserId, ConversationId, ProjectId, ToolId, RequiredPermission)` applies only where `Status=Active AND RevokedAt IS NULL`. It prevents two active grants for the exact tuple. Multiple revoked historical rows are permitted. A future reader must still reject malformed or ambiguous data rather than select a first match, and a future writer must handle unique-constraint conflicts safely. A grant for a different requirement is separate and must not be treated as an implicit higher/lower permission.

## Migration and compatibility

`20261014000000_AddToolPermissionGrants` creates only the new table, three Restrict FKs, checks, ordinary FK indexes, and the partial active-grant unique index. Down removes only that new table. It does not alter/drop `Permissions`, copy its rows, seed a tool grant, or assume existing generic permissions authorize tools. The migration was **not applied** to PostgreSQL in this step.

The repository had no EF migrations or model snapshot before this targeted migration. Its deployment therefore requires an explicit review of the target database's existing base schema and migration history. Future EF migration scaffolding must establish a reviewed baseline/snapshot before generating additional migrations; the absence of a snapshot must not be mistaken for permission to recreate existing tables. No production database compatibility has been verified live.

## Runtime boundary and Step 13B

Normal `Program.cs` still registers no production `IToolPermissionSource`. Both startup and informational permission readiness remain false; `ProductionAdaptersEnabled`, `ToolExecutionEnabled`, and `SafeFileAccessEnabled` default false. No production permission enforcement, tool execution, file access, or OpenAI/Ollama call is enabled. Existing project-file permission semantics are unchanged.

Step 13B must review the migration against a controlled PostgreSQL database, establish the EF baseline strategy, implement an exact read-only source with ownership and current active/non-revoked checks, reject malformed/duplicate/conflicting data, propagate cancellation, test SQL translation and concurrency, and separately review grant issuance/revocation lifecycle before any runtime registration or readiness change. No automatic grant creation or role/owner/approval bypass may be introduced.

Package-free Step 13A checks inspect EF design-time metadata, the separate generic permission model, database constraints, FKs, uniqueness, migration text, absent seed data/adapter, and disabled runtime gates. These offline checks do not verify PostgreSQL migration application, query behavior, or live authorization.

With .NET SDK 10.0.302, the requested default solution build exited 1 with zero reported warnings or errors in this command environment. The single-worker fallback succeeded with zero warnings and errors. The plain security-check command had the same build anomaly; the fallback passed **1,383 offline checks**, including compiled migration-operation checks. No migration was applied to a database. `git diff --check` passed.
