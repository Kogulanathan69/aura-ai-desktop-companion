# Step 17W — Permission production readiness

## Decision

**Permission=true.** The exact scoped production source, policy and validator
are registered and cataloged. The generated-fixture live revocation harness
passed against the verified disposable target. It proved an active allow,
persisted revocation and fresh denial through the same validator instance,
along with the required exact and negative bindings. Ownership remains true.

## Grant model and exact decision

`ToolPermissionGrantRecord` stores `Id`, `UserId`, `ConversationId`, `ProjectId`,
canonical `ToolId`, numeric `RequiredPermission`, `Status`, `RevokedAt`,
`CreatedAt` and `GrantedAt`. It has foreign keys to User, Conversation and
Project. `Status=Active` requires `RevokedAt=null`; `Status=Revoked` requires a
non-null `RevokedAt`. A partial unique index prevents duplicate active grants
for the exact user/conversation/project/tool/permission tuple. Two revoked rows
may exist, but neither can authorize. There is no expiry field, updated time or
grant capability stored separately; expiry denial is **not applicable**.

The first reviewed tool candidate is `owned-project-metadata-read`, capability
`ProjectRead`, requiring `OwnedProjectRead`. The source performs one bounded,
no-tracking database query over the exact user, conversation, project, tool and
permission tuple. It requires an active local user, exact owned Project
conversation and owned project, plus an active grant with no revocation time.
Wrong user, project, conversation, tool or requirement, missing grant, revoked
grant and malformed scope deny. The source rejects ambiguous active results.
The partial unique index prevents a deterministic duplicate-active fixture;
the index must not be weakened to manufacture one. Grant IDs are result data,
never caller-supplied authority. Generic `Permissions` rows do not count.

`ToolExecutionPermissionValidator` reads the source on every decision and uses
`ExactToolPermissionPolicy` to compare the returned binding and lifecycle.
There is no permission-result cache or fallback allow. Ownership remains a
separate prerequisite; an owned project alone does not grant tool permission.
ActionId and ApprovalId are not grant fields and are not added to permission
storage.

## Production DI, catalog and readiness

API startup now calls `AddPermissionRuntimeFoundation`, which registers exactly
one scoped `IToolPermissionSource → ToolPermissionPersistenceSource`,
`IToolPermissionPolicy → ExactToolPermissionPolicy`, and
`IToolExecutionPermissionValidator → ToolExecutionPermissionValidator`. The
helper requires the reviewed scoped database alias and rejects duplicate or
conflicting permission registrations. The inspector catalogs those exact types
and lifetimes and requires Ownership and complete live permission evidence.
Its live evidence catalog records the reviewed PASS categories and verified
target identity. The complete production graph therefore derives Permission as
true. A missing, fallback, duplicate, unexpected or wrong-lifetime node cannot
satisfy the graph rule.

## Disposable live harness

```powershell
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -- --live-production-permission-revocation-evidence
```

The new command requires `AURA_LIVE_PRODUCTION_PERMISSION_EVIDENCE=true`,
`AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true`,
`ConnectionStrings__DefaultConnection`, `AURA_EXPECTED_DATABASE_OID`, and
`AURA_EXPECTED_SYSTEM_IDENTIFIER`. The reviewed loopback non-TLS route also
requires `AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE=true`. It verifies the exact
database name `aura_disposable_test`, OID `24576` and system identifier
`7696813047573034172` before writes. It creates generated users, projects,
conversations and one exact active grant. It exercises the production validator
for active allow, foreign user, wrong project/conversation/tool/permission and
unknown-grant denials. It then persists `Status=Revoked` with `RevokedAt`,
re-evaluates through the same validator instance and requires denial. Cleanup
removes only its generated rows. Output contains fixed categories, not
credentials or row IDs.

The operator ran the command against `aura_disposable_test`, OID `24576`, system
identifier `7696813047573034172`. Every emitted category passed: connectivity,
schema, fixture setup, active and exact allows, foreign/wrong/unknown denials,
persisted revoked-grant denial, revocation after prior allow, negative bindings,
and cleanup. **Live result: PASS.** Credentials and process environment variables
were cleared afterward. The historical read-only
`--live-production-permission-evidence` command remains separate. Grant issuance
and revocation authorization are not added in this step.

Exact sanitized operator result:

```text
DB CONNECTIVITY: PASS
SCHEMA CHECK: PASS
FIXTURE SETUP: PASS
ACTIVE GRANT ALLOW: PASS
OWNED USER GRANT ALLOW: PASS
EXACT PROJECT GRANT ALLOW: PASS
EXACT TOOL GRANT ALLOW: PASS
EXACT PERMISSION ALLOW: PASS
FOREIGN USER GRANT DENY: PASS
WRONG PROJECT DENY: PASS
WRONG CONVERSATION DENY: PASS
WRONG TOOL DENY: PASS
WRONG PERMISSION DENY: PASS
UNKNOWN GRANT DENY: PASS
REVOKED GRANT DENY: PASS
REVOCATION AFTER PRIOR ALLOW DENY: PASS
NEGATIVE BINDINGS: PASS
CLEANUP: PASS
RESULT: PASS
```

## Remaining composition

Ownership=true. Permission=true. ExecutionState, AuditSink, AuditDelivery,
Reconciliation, Verification, ExecutionPolicy, ToolHandlers and
TrustedExecution remain false. CompositionComplete=false, runtime flags=false
and activation is denied. VerificationWorkflowService does not use a permission
grant as its ownership proof; ToolRegistry, independent Verified policy and its
workflow-to-writer path remain separate Verification blockers.
