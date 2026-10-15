# Step 14D — read-only live-evidence release gate

## Release verdict

**OWNERSHIP LIVE RUN: BLOCKED**

**PERMISSION LIVE RUN: BLOCKED**

This is a human/operator release decision for only the two read-only harnesses in [Step 14A](STEP-14A-LIVE-EVIDENCE-PLAN.md). At this review, the target classification is **UNKNOWN**; no non-production target, read-only account, privilege report or positive fixtures have been reviewed. The required local environment variables are absent. Both live opt-in flags are absent, which is the expected pre-release state. This document does not authorize a live run. The commands below were **not run in Step 14D**. No PostgreSQL connection, migration or runtime change occurred. [Step 14B](STEP-14B-LIVE-EVIDENCE-PREFLIGHT.md) and [Step 14C](STEP-14C-CONTROLLED-LIVE-ENVIRONMENT-SETUP.md) remain the preflight and setup references. Production tool execution remains **NOT READY** under [Step 13L](STEP-13L-SECURITY.md).

## Shared human-confirmed prerequisites

- [ ] An operator independently confirms the exact database identity and owner from approved evidence and classifies it **CONTROLLED READ-ONLY TEST**, non-production. The connection variable alone is not classification evidence.
- [ ] A dedicated read-only verification credential exists and is reviewed; it is not a production application account. Effective privileges, including inherited roles and default grants, are documented by a DBA-reviewed role definition, privilege report, account policy or equivalent approved infrastructure evidence.
- [ ] The account has CONNECT to this test database, schema USAGE and only necessary SELECT privileges. Confirm **no INSERT, UPDATE, DELETE, TRUNCATE, CREATE/schema-modification or migration privilege**. An account name containing “read-only” is insufficient.
- [ ] The connection string will be supplied only in the approved local operator session, points to the independently confirmed target, and will not be committed, logged or included in shared evidence. Credentials and identifiers remain outside source control.
- [ ] The operator has a reviewed secret-handling process: no password, connection string, host, username, GUID, ToolId, permission value or JWT is printed in the release record; local variables are cleared after the session. Any target/privilege attestations are stored in an approved evidence location.
- [ ] The relevant schema and test data are safe to query, and the operator understands that read-only harness PASS is not real Supabase JWT evidence.

This release gate cannot establish any of these facts by inspecting environment variables. The human evidence must be reviewed separately before changing a verdict to READY.

## Presence-only environment matrix

Presence was inspected in this review's command process only, without printing or parsing values. Recheck it in the eventual operator session. **Presence does not establish validity, ownership, account privileges or target safety.** The optional grant ID is not required for permission readiness when no exact grant ID is selected.

| Harness | Variable | Presence now | Required for READY? |
| --- | --- | --- | --- |
| Both | `ConnectionStrings__DefaultConnection` | ABSENT | Yes |
| Ownership | `AURA_VERIFY_AUTH_USER_ID` | ABSENT | Yes |
| Ownership | `AURA_VERIFY_INTERNAL_USER_ID` | ABSENT | Yes |
| Ownership | `AURA_VERIFY_CONVERSATION_ID` | ABSENT | Yes |
| Ownership | `AURA_VERIFY_PROJECT_ID` | ABSENT | Yes |
| Permission | `AURA_VERIFY_PERMISSION_USER_ID` | ABSENT | Yes |
| Permission | `AURA_VERIFY_PERMISSION_CONVERSATION_ID` | ABSENT | Yes |
| Permission | `AURA_VERIFY_PERMISSION_PROJECT_ID` | ABSENT | Yes |
| Permission | `AURA_VERIFY_PERMISSION_TOOL_ID` | ABSENT | Yes |
| Permission | `AURA_VERIFY_PERMISSION_REQUIREMENT` | ABSENT | Yes |
| Permission | `AURA_VERIFY_PERMISSION_GRANT_ID` | ABSENT | Optional; if supplied it must identify the reviewed exact grant |
| Ownership opt-in | `AURA_LIVE_OWNERSHIP_VERIFY` | ABSENT | Must remain unset during release review; set to exact `true` only immediately before an approved run |
| Permission opt-in | `AURA_LIVE_PERMISSION_VERIFY` | ABSENT | Must remain unset during release review; set to exact `true` only immediately before an approved run |

If either opt-in flag is unexpectedly already exact `true`, record a **review warning** and keep the affected run BLOCKED pending investigation; do not execute a harness. A present but non-`true` flag also needs operator review before the run. The source checks exact lowercase `true`; it otherwise reports `SKIPPED`, which is not evidence. This review did not set or change any variable.

## Ownership fixture checklist

- [ ] A known controlled test `AuthUserId` and matching internal `Users.Id` are available locally; neither value is placed in this document.
- [ ] A known `ConversationId` and `ProjectId` are available locally and safe to query without printing sensitive row contents.
- [ ] A human reviews the full relation: `AuthUserId` → `Users.Id`; that user owns the conversation; the conversation belongs to the project; and the same user owns the project.
- [ ] The reviewed positive fixture is active and suitable for the harness's mapping, ownership and negative checks.

The ownership harness uses a fixed current-user test seam. It does not accept or validate a real JWT in this run.

## Permission fixture checklist

- [ ] Known controlled test `UserId`, `ConversationId`, `ProjectId`, canonical `ToolId` and `RequiredPermission` are available locally; optional `GrantId` is provided only when exact positive validation needs it.
- [ ] The user/conversation/project relationship and the grant's exact binding are manually reviewed.
- [ ] An active, non-revoked positive grant exists and is intended for testing. A missing grant produces `NO TEST GRANT`, not PASS.
- [ ] `ToolId` is expected to pass the authoritative `ToolIdentifier` parser, and `RequiredPermission` is exactly one reviewed enum name: `OwnedProjectRead`, `OwnedProjectWrite` or `ExternalAction`.
- [ ] The permission table/schema and grant fixture are independently reviewed; no sensitive row contents need to be printed.

## Release decision logic and future commands

**Ownership READY** requires every shared human prerequisite, every ownership fixture check, presence of the connection and all four ownership variables in the actual operator session, approved secret handling, and no unexpectedly active live flag. Otherwise **BLOCKED**. The current blockers are unclassified target, unreviewed privileges/credential, unreviewed fixture relationship, absent connection and four IDs, and unreviewed secret-handling process. The absent live flag is expected and is not a blocker.

**Permission READY** requires every shared human prerequisite, every permission fixture check, presence of the connection and all five required permission variables in the actual operator session, approved secret handling, and no unexpectedly active live flag. Otherwise **BLOCKED**. The current blockers are unclassified target, unreviewed privileges/credential, unreviewed active grant and relationship, absent connection and five required values, and unreviewed secret-handling process. The optional grant ID is absent but is not itself a blocker. The absent live flag is expected.

Only **after** the corresponding verdict is changed to READY by a human review may an operator prepare the exact opt-in flag and consider its documented command. These commands are shown for future use and were **not executed in Step 14D**:

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -- --live-ownership
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -- --live-permission
```

The commands query only; they do not create fixtures or apply migrations. Retain sanitized result categories and exit codes according to Step 14A. A future `PASS` still needs human evidence review. Real Supabase JWT verification is a separate task and remains blocked; neither read-only harness closes it or changes production runtime readiness.
