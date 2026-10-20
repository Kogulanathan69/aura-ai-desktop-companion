# Step 17V — Ownership production readiness

## Decision

**Ownership=true** in the reviewed production composition graph. The previous
partial state had a scoped graph but no ActionId binding or live bearer evidence.
Existing-action workflows now require exact action ownership, and the signed-JWT
HTTP harness passed on the verified disposable target. The inspector derives
Ownership from the exact graph, catalog and recorded live evidence; registration
alone cannot assert it. All other production composition fields, runtime flags
and activation remain false or denied.

## Trusted identity and local mapping

`Program.cs` configures JWT bearer validation against the HTTPS Supabase authority
and configured audience, requires HTTPS metadata, and disables inbound claim
remapping. `HttpCurrentUserContext` accepts exactly one authenticated identity and
one raw `sub` claim belonging to it. The subject must parse as a nonempty GUID.
It never reads a user ID from a request body, scope, header or email claim.

`AuthenticatedUserMappingSource` reads active `User` rows by `AuthUserId` with a
bounded, no-tracking `Take(2)` query. No row, an inactive row, an empty subject,
multiple rows, an unavailable database, or a malformed principal returns
unavailable/denied. A caller cannot choose the mapped local `User.Id`; the database
owns that association. Cancellation is propagated.

`Users.AuthUserId` has a unique index. Two persisted rows with the same subject
are structurally prevented by the production schema, so an ambiguous-subject
fixture is **not applicable**. The bounded lookup still denies ambiguity if the
schema invariant is ever broken. Missing and unknown subjects deny.

## Ownership chain and validator

`OwnershipPersistenceSource` requires an active local user, a Project conversation
owned by that user with the exact `ProjectId`, and a project owned by that same
user. A missing, foreign or mismatched row denies. `ExactOwnershipScopeValidator`
requires the authenticated mapped user to match `ActionScope.UserId` and the
source to return the exact requested user/conversation/project binding with all
ownership predicates true. Errors fail closed.

`ActionScope` remains the pre-action user/conversation/project scope, because an
action proposal has no persisted ActionId yet. The new `ExactActionOwnershipScope`
is constructed from a server-owned `ActionDescriptor`, carries its exact
`ActionIdentifier`, and has no public constructor or setters. The validator's
`ValidateActionAsync` first validates the base scope and then requires the
database action row to match action ID, local user, conversation and project in
one bounded query with the active user and owned chain. Missing, foreign,
malformed and mismatched rows deny. Tool and approval ownership are outside this
contract; permission and approval remain separate responsibilities.

`ActionLifecycleService.TransitionAsync`, `ApprovalWorkflowService.DecideAsync`,
`TrustedToolExecutionService.ExecuteAsync`, and
`VerificationWorkflowService.VerifyAsync` now call exact action ownership before
action-sensitive work. Approval, execution and verification repeat it at their
existing revalidation point. Reconciliation first validates the persisted
snapshot against the request and stored state, derives the exact action scope
from that snapshot, and checks ownership before policy assessment or evidence
writing. A requested ActionId selects a candidate; the database proves ownership.

## DI and reviewed catalog

Normal API startup calls `AddOwnershipRuntimeFoundation` after registering the
scoped `IAuraDbContext` and exact scoped `AuthenticatedUserMappingSource`. The helper
registers scoped `HttpCurrentUserContext`, `OwnershipPersistenceSource` and one
scoped `ExactOwnershipScopeValidator`, with scoped aliases for its exact action
contract and five existing validator
interfaces. It rejects conflicting registrations. No test principal, fake user,
fallback allow, worker or migration is installed.

The composition inspector names the exact mapping, context, source, validator
and context factory types in the ownership reviewed catalog. It requires the
reviewed ASP.NET Core authentication service, HTTP context accessor, scoped
database context, singleton factory, exact scoped implementations, and all six
validator aliases bound to the one concrete scoped validator. Missing, duplicate,
wrong-lifetime, fallback or unexpected registrations deny. The reviewed live
evidence catalog and verified target identity are also required. The normal API
graph satisfies these requirements, so its Ownership field is true.

## Live evidence

Existing gated source command:

```powershell
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -- --live-production-ownership-evidence
```

It requires `AURA_LIVE_PRODUCTION_OWNERSHIP_EVIDENCE=true`,
`AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true`, a connection string, and explicit
existing-row identifiers. It performs read-only mapping and project/conversation
checks. These are narrower than Step 17V's desired generated fixtures, foreign
action checks and authenticated HTTP/JWT path. Its success now reports
`SOURCE RESULT: PASS` and `PRODUCTION OWNERSHIP: INCOMPLETE` with a nonzero exit.

The new signed JWT HTTP command is:

```powershell
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -- --live-signed-jwt-ownership-evidence
```

It requires the two gates above plus `ConnectionStrings__DefaultConnection`,
`AURA_EXPECTED_DATABASE_OID`, and `AURA_EXPECTED_SYSTEM_IDENTIFIER`. For the
reviewed loopback non-TLS disposable target it also requires
`AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE=true`. It validates the actual database
name, OID and system identifier before writing. It creates uniquely identified
disposable user, project, conversation and action rows and removes them in a
`finally` block. An isolated loopback ASP.NET Core host uses the real JWT bearer
middleware with a runtime-generated signing key and exercises the production
ownership graph through authenticated HTTP requests. The key is never in
production configuration. It checks signed-token acceptance, subject mapping,
owned/foreign/missing/mismatched row chains, unauthenticated requests, malformed
tokens and unknown subjects. The unique `Users.AuthUserId` index prevents a
deterministic ambiguous-subject fixture. No arbitrary `ClaimsPrincipal` is used.

The operator manually ran the signed-JWT command against `aura_disposable_test`
(database OID `24576`, system identifier `7696813047573034172`) and supplied
sanitized output. Credentials and process environment variables were cleared.
The following exact categories were reported **PASS**:

```text
DB CONNECTIVITY: PASS
SCHEMA CHECK: PASS
FIXTURE SETUP: PASS
SIGNED JWT ACCEPT: PASS
JWT SUBJECT MAP: PASS
OWNED PROJECT ALLOW: PASS
FOREIGN PROJECT DENY: PASS
UNKNOWN PROJECT DENY: PASS
OWNED CONVERSATION ALLOW: PASS
FOREIGN CONVERSATION DENY: PASS
PROJECT CONVERSATION MISMATCH DENY: PASS
OWNED ACTION ALLOW: PASS
FOREIGN ACTION DENY: PASS
ACTION CONVERSATION MISMATCH DENY: PASS
ACTION PROJECT MISMATCH DENY: PASS
ACTION USER CHAIN MISMATCH DENY: PASS
UNAUTHENTICATED DENY: PASS
MALFORMED TOKEN DENY: PASS
UNKNOWN SUBJECT DENY: PASS
NEGATIVE BINDINGS: PASS
CLEANUP: PASS
RESULT: PASS
```

The malformed-token HTTP 401 and unknown-subject HTTP 403 logs are expected
negative-path results. This is operator-supplied live evidence; this continuation
did not rerun the disposable database command.

## Verification impact and remaining work

The `IVerificationScopeValidator` alias and exact action ownership are now
production-ready dependencies. Verification remains false: ToolRegistry,
independent Verified policy, and the workflow-to-writer path still require
separate review. Permission, ExecutionState, AuditSink, AuditDelivery,
Reconciliation, ExecutionPolicy, ToolHandlers and TrustedExecution remain false;
CompositionComplete is false. Runtime flags remain false and activation is denied.
