# Step 17T — database-controlled Project version

## Design and write path

`Project.Version` is a required `bigint`. The migration initializes every existing row to `1` and gives new rows a database default of `1`. A `BEFORE INSERT OR UPDATE` trigger rejects an inserted value other than `1`; on update it checks that the incoming value equals the stored value, rejects exhaustion at `long.MaxValue`, and assigns the old value plus one. Thus each successful authoritative update advances the token once, including direct SQL and EF updates. The positive check constraint rejects invalid values. The mapped property has a private setter, is generated on add/update, and is an EF concurrency token. Application request models do not expose Version.

The production Project update path is `ProjectService.UpdateAsync`; it changes fields and calls `SaveChangesAsync` without assigning Version. EF's concurrency predicate rejects a stale tracked write. The live harness also uses `ExecuteUpdateAsync`; the database trigger advances Version there. `UpdatedAt` remains observational metadata.

## Migration

ID: `20261019000001_AddProjectVersion`. Up adds only the Project column, positive constraint, function, and update trigger. Down removes those objects in reverse order. Down would discard the version token and must not be used while version-bound proof is in use. No API startup migration or `EnsureCreated` was added.

The read-only migration baseline now expects eight exact IDs, a required `int8` Project Version, default `1`, positive constraint, and enabled insert/update trigger with guarded initialization and increment. The prior seven IDs remain in order. The operator-only apply command checks the disposable acknowledgement, expected database OID and system identifier, exact seven-applied/one-pending migration state, and the reviewed loopback non-TLS exception; remote targets require VerifyFull.

## Proof and persistence

The authoritative evidence source reads Project.Version alongside Project identity and UpdatedAt. `OwnedProjectMetadataProof` carries the positive immutable `ProjectVersion`, execution version, and fixed proof source/version. The observation and proof versions must match before Verified evidence is issued. The workflow repeats that binding check. The persistence writer locks the exact owned Project row and requires its current Version to equal the proof's ProjectVersion before inserting Verified. It does not mutate execution state or retry a handler.

## Live evidence

**PROVEN by sanitized operator report:** The Project Version migration apply command returned `RESULT: PASS` on `aura_disposable_test` (OID `24576`, system identifier `7696813047573034172`). The refreshed eight-migration baseline returned PASS for connectivity, history, baseline, core tables, key constraints, and security schema. Credentials and process environment were cleared. This Codex process did not independently rerun those commands.

**Historical first run:** The first revised independent verification run returned `RESULT: INCOMPLETE`. Project Version initialization, increment, stale rejection, and overlapping update passed; `CONCURRENT PROJECT UPDATE` and `FRESH PROJECT VERSION VERIFIED` failed. This result was superseded by the successful rerun below.

The harness fresh-proof guard compared `proof.ProjectUpdatedAt` from PostgreSQL with the local `DateTime.UtcNow.AddSeconds(-1)` used in `ExecuteUpdateAsync`. PostgreSQL stores timestamps at microsecond precision, so the reread could differ at submicrosecond ticks. The fresh category returned before attempting the writer. The older concurrent category was the conjunction of the four new version checks, including the fresh category.

The fix updates only `Project.Name` while leaving `UpdatedAt` unchanged. It verifies that the fresh proof has Version N+1 and the same persisted timestamp as the old proof, then issues and writes Verified using a fresh source context. The stale proof also has that same timestamp, so its rejection demonstrates that Project.Version is the authoritative freshness token. No stale-proof rule or writer version recheck was weakened.

**PROVEN by sanitized operator rerun:** The revised independent verification harness returned `RESULT: PASS` on the same verified target. All 31 reported categories passed:

```text
DB CONNECTIVITY: PASS
SCHEMA CHECK: PASS
FIXTURE SETUP: PASS
AUTHORITATIVE PROJECT OBSERVATION: PASS
INDEPENDENT PROOF CREATION: PASS
HANDLER RESULT INDEPENDENCE: PASS
EXACT EXECUTION VERSION: PASS
EXACT PROOF SOURCE: PASS
EXACT PROOF VERSION: PASS
VERIFIED ASSESSMENT PERSISTENCE: PASS
NEGATIVE ACTION BINDING: PASS
NEGATIVE USER BINDING: PASS
NEGATIVE CONVERSATION BINDING: PASS
NEGATIVE PROJECT BINDING: PASS
NEGATIVE TOOL BINDING: PASS
NEGATIVE APPROVAL BINDING: PASS
NEGATIVE EXECUTION BINDING: PASS
STALE EXECUTION VERSION: PASS
WRONG PROOF SOURCE: PASS
WRONG PROOF VERSION: PASS
MISSING PROOF: PASS
FAILED EXECUTION: PASS
REPEATED ASSESSMENT: PASS
CONCURRENT PROJECT UPDATE: PASS
STATE IMMUTABILITY: PASS
CLEANUP: PASS
PROJECT VERSION INITIALIZATION: PASS
PROJECT VERSION INCREMENT: PASS
STALE PROJECT VERSION REJECTION: PASS
OVERLAPPING PROJECT UPDATE: PASS
FRESH PROJECT VERSION VERIFIED: PASS
RESULT: PASS
```

The reported target remained `aura_disposable_test`, OID `24576`, system identifier `7696813047573034172`. Credentials and process variables were cleared. This Codex process did not independently rerun PostgreSQL evidence. The command used for the successful rerun was:

```powershell
$env:AURA_LIVE_PRODUCTION_INDEPENDENT_VERIFICATION_EVIDENCE = 'true'
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-independent-verification-evidence
```

The extended harness checks initialization, increment, stale rejection, overlapping independent observer and updater contexts, fresh Verified persistence, execution-state immutability, and cleanup. The observer keeps a repeatable-read transaction open across the updater's commit; the writer uses its own connection. Any missing category returns `RESULT: INCOMPLETE`.

## Readiness

**PROVEN offline:** source migration shape, EF concurrency metadata, proof binding, locked writer version comparison, gated command behavior, and unchanged fail-closed production composition. The offline suite passes 2,528 checks.

**PROVEN live by operator report:** the database-controlled Project.Version migration and eight-migration baseline passed. The harness proved initialization, increment, stale Project.Version rejection with unchanged UpdatedAt, overlapping independent transactions, fresh Version Verified persistence, execution-state immutability, and cleanup. `Project.Version` is the authoritative project freshness token; `UpdatedAt` remains observational metadata.

**NOT PROVEN:** production Verification factory and exact DI/catalog registration, which belong to Step 17U. A separate stale tracked EF write test was not part of the live harness. Verification remains `false` pending Step 17U review.

**NOT APPLICABLE:** Step 17U factory/DI registration is outside this continuation. Production runtime flags remain false and activation remains denied.


