# Step 14C — controlled live environment setup plan

## Purpose and current verdict

**LIVE ENVIRONMENT SETUP: NOT READY.** This document defines the operator preparation needed before the [Step 14A live evidence sequence](STEP-14A-LIVE-EVIDENCE-PLAN.md). [Step 14B](STEP-14B-LIVE-EVIDENCE-PREFLIGHT.md) found no classified database, reviewed accounts, fixture IDs or live JWT evidence. This step creates no target or account, sets no environment variable, connects to no database, runs no harness and applies no migration. Production tool execution remains **NOT READY** under [Step 13L](STEP-13L-SECURITY.md). A plan, a gate flag, a database-name prefix, or a harness `SKIPPED` result is not live PASS evidence.

## Two separate database targets and credentials

| Target | Use | Required classification and account |
| --- | --- | --- |
| **CONTROLLED READ-ONLY TEST DATABASE** | Ownership and permission verification (A/B). These harnesses issue read-only queries. | Human-reviewed non-production host/database and owner; a dedicated `READ_ONLY_VERIFY` PostgreSQL credential; known positive fixtures. Grant CONNECT to the test database, USAGE on required schema and SELECT on required tables/views. Withhold CREATE, INSERT, UPDATE, DELETE, TRUNCATE and migration privileges. Verify effective privileges, including role inheritance and default grants, before release. |
| **CONTROLLED DISPOSABLE WRITE DATABASE** | Execution-state concurrency, audit/outbox, and approved migration verification (C/D/optional D2). These harnesses write temporary fixture rows and perform exact-ID cleanup. | Human-reviewed, explicitly disposable non-production target with no valuable data; preferably name beginning `aura_disposable_` (required by D/D2 source). A separate limited `DISPOSABLE_VERIFY` credential needs CONNECT, schema USAGE and only the SELECT/INSERT/UPDATE/DELETE privileges on fixture-related tables that its harness actually uses; sequence rights only if the schema needs them. Grant migration DDL rights only for separately approved Step 13J application, using a separately controlled migration role if practical. No superuser, createdb unless separately necessary, or production database access. |

Never reuse a production application account. Keep the read-only and write credentials separate, and use separate PowerShell sessions or clear and recheck `ConnectionStrings__DefaultConnection` between targets. The application-level gate flags and `aura_disposable_` prefix are guardrails, not proof of database identity or disposability. Review the actual target and account privileges through an approved administrative process. This plan executes no GRANT or account-creation statement.

## Positive fixture inventory

All identifiers must come from a reviewed controlled test dataset and be recorded in an approved location outside source control. Do not invent production IDs or put real IDs or row contents in this document.

| Harness | Required existing positive fixture |
| --- | --- |
| Ownership A | Active external `AuthUserId`, matching internal `Users.Id`, owned `ConversationId`, and owned `ProjectId`. Verify the subject-to-user and ownership relationships together, plus suitable negative cases. |
| Permission B | `UserId`, `ConversationId`, `ProjectId`, canonical `ToolId`, exact `RequiredPermission` (`OwnedProjectRead`, `OwnedProjectWrite`, or `ExternalAction`), and optionally `GrantId`. Verify that a current exact grant exists and is active/non-revoked; no grant means the harness cannot PASS. |

The ownership harness uses a fixed current-user seam and does not validate a real JWT. Fixture selection must not be mistaken for Supabase authentication evidence.

## Migration baseline decision tree

The repository contains ordered targeted migrations `20261014000000` (tool grants), `00001` (execution states), `00002` (security persistence), `00003` (audit outbox), and `00004` (Step 13J delivery constraint). It lacks a reviewed historical core-schema baseline and EF model snapshot. Source files do not establish a database's applied state.

1. **A reviewed disposable database already has the core schema.** Independently inspect schema, ownership, constraints, and actual `__EFMigrationsHistory`; compare exact applied migration IDs and order with source before any live command. Confirm C's 00001 requirements and D's through-00003 baseline. For D, inspect historical invalid `Failed`/`DeliveredAt` rows and determine whether 00004 is already applied. Do not fabricate history or mark a migration applied manually without a separate reviewed baseline procedure.
2. **No reviewed baseline database exists.** C/D/D2 and live migration claims remain blocked. Do not use `EnsureCreated`, fake `__EFMigrationsHistory`, or blindly apply 00000–00004 to an empty database. A database name alone does not supply the missing core schema.
3. **A future baseline must be created.** Conduct a separate migration-baseline review that maps the existing core schema to a trustworthy starting point, defines reproducible preparation, and reviews schema/history compatibility before any live migration claim. This step does not perform that work.

Step 13J application is a separate decision. D may target only 00004 after exact history, schema and data checks, and only when the operator has explicitly approved and supplied `AURA_ALLOW_AUDIT_OUTBOX_MIGRATION`. D2 never applies migrations and requires 00004 already applied. The hardening migration is not rolled back by fixture cleanup.

## Controlled Supabase JWT setup

A separate future task must define and review a real-token path: a non-production Supabase project and HTTPS issuer/authority, expected audience, actual signing configuration, a dedicated test user, a valid short-lived token, and a raw JWT `sub` matching the controlled dataset's `Users.AuthUserId`. Verify the `sub` → local `Users.Id` → owned resource chain through the intended API flow. Keep tokens and signing material out of source control, documentation, terminal output and shared logs; do not reuse a production token. The current A/B harnesses do not constitute a full live JWT harness, so Step 13L JWT evidence remains blocked even if they later PASS.

## Local environment-variable templates

The following are **documentation-only PowerShell templates** for a future approved operator session. They contain no credentials, IDs or actual target. Do not execute them until the corresponding checklist and release criteria are met. Assign the exact gate value `true` only immediately before the intended live run. Prefer an approved local secret manager; if using session environment variables, avoid pasting actual secrets into recorded terminal history and clear variables when the session ends. Never commit `.env`, secret-bearing `appsettings.json`, passwords, tokens or connection strings.

**A — ownership, read-only target:**

```powershell
$env:ConnectionStrings__DefaultConnection = '<read-only-test-connection-set-locally>'
$env:AURA_VERIFY_AUTH_USER_ID = '<reviewed-test-id>'
$env:AURA_VERIFY_INTERNAL_USER_ID = '<reviewed-test-id>'
$env:AURA_VERIFY_CONVERSATION_ID = '<reviewed-test-id>'
$env:AURA_VERIFY_PROJECT_ID = '<reviewed-test-id>'
$env:AURA_LIVE_OWNERSHIP_VERIFY = 'true'
```

**B — permission, read-only target:**

```powershell
$env:ConnectionStrings__DefaultConnection = '<read-only-test-connection-set-locally>'
$env:AURA_VERIFY_PERMISSION_USER_ID = '<reviewed-test-id>'
$env:AURA_VERIFY_PERMISSION_CONVERSATION_ID = '<reviewed-test-id>'
$env:AURA_VERIFY_PERMISSION_PROJECT_ID = '<reviewed-test-id>'
$env:AURA_VERIFY_PERMISSION_TOOL_ID = '<reviewed-canonical-tool-id>'
$env:AURA_VERIFY_PERMISSION_REQUIREMENT = '<reviewed-exact-requirement>'
$env:AURA_VERIFY_PERMISSION_GRANT_ID = '<optional-reviewed-test-id>'
$env:AURA_LIVE_PERMISSION_VERIFY = 'true'
```

Omit `AURA_VERIFY_PERMISSION_GRANT_ID` entirely if no exact grant ID is being selected; do not leave the placeholder as a live value.

**C — execution state, disposable write target:**

```powershell
$env:ConnectionStrings__DefaultConnection = '<disposable-test-connection-set-locally>'
$env:AURA_EXECUTION_STATE_DB_IS_DISPOSABLE = 'true'
$env:AURA_LIVE_EXECUTION_STATE_VERIFY = 'true'
```

**D — audit/outbox hardening, disposable write target:**

```powershell
$env:ConnectionStrings__DefaultConnection = '<disposable-test-connection-set-locally>'
$env:AURA_AUDIT_OUTBOX_DB_IS_DISPOSABLE = 'true'
$env:AURA_LIVE_AUDIT_OUTBOX_VERIFY = 'true'
$env:AURA_ALLOW_AUDIT_OUTBOX_MIGRATION = 'true' # ONLY for separately approved pending Step 13J
```

Omit `AURA_ALLOW_AUDIT_OUTBOX_MIGRATION` if 00004 is already applied or migration application is not approved. **Optional D2** uses the same two audit gate variables but reads `AURA_AUDIT_OUTBOX_DISPOSABLE_CONNECTION` instead of `ConnectionStrings__DefaultConnection`; its target must also be independently verified and have 00004 applied.

After a verification session, remove each variable set for that session (for example, `Remove-Item Env:VARIABLE_NAME` with the reviewed exact names) or close the isolated PowerShell process. Do not print secret values while checking presence or cleanup.

## Human pre-run checklist

**Read-only A/B:**

- [ ] Target host/database and owner reviewed as non-production; connection points to that exact target.
- [ ] Dedicated account is read-only in practice, with no write, schema modification or migration privilege.
- [ ] Positive ownership/permission fixtures and exact IDs are reviewed outside source control.
- [ ] Relevant schema and migration history are compatible with the selected harness.
- [ ] Secrets and identifiers will not be put in terminal history or documentation where avoidable.

**Disposable C/D/D2:**

- [ ] Target is independently confirmed disposable, non-production and contains no valuable data; no backup is needed because it is genuinely disposable.
- [ ] Dedicated limited write account and exact host/database are reviewed; no production access.
- [ ] Fixture insert/update/delete and exact-ID cleanup scope are reviewed; failures have a containment/disposal procedure.
- [ ] Core baseline, actual migration history, required schema/constraints and Step 13J state are reviewed.
- [ ] For D, invalid historical rows are reviewed and the Step 13J migration approval decision is explicit; migration DDL privilege is supplied only if approved.
- [ ] `aura_disposable_` name requirement is met for D/D2, without treating the prefix as proof of safety.

## Release criteria and blockers today

| Run | May change from BLOCKED to READY TO RUN only when |
| --- | --- |
| A/B | Controlled non-production read-only target exists; account's effective privileges are reviewed; exact positive fixtures are known; the intended connection is configured locally; operator confirms target identity and no production access. B also needs its exact active grant and compatible schema. |
| C | Dedicated disposable database exists; limited write account and cleanup are reviewed; core baseline and 00001 migration/schema are compatible; disposable acknowledgement is intentionally prepared. |
| D | Independently confirmed `aura_disposable_` target exists; limited write/migration roles are reviewed; core baseline through 00003 is verified; historical invalid rows and 00004 state are reviewed; Step 13J migration decision is explicit; disposable acknowledgement is intentionally prepared. |
| D2 | Same disposable classification, limited write account and cleanup; all targeted migrations through 00004 already applied; dedicated D2 connection configured locally. |

**Today:** Step 14B found no classified target, reviewed read-only or disposable account, positive fixture IDs, configured local connection, verified baseline/history, Step 13J applied-state evidence, or real Supabase JWT evidence. Accordingly A, B, C, D and D2 remain blocked. The operator must follow Step 14A's A → B → C → D → optional D2 → final evidence-review order only after each run's conditions are met. Neither this setup plan nor later PostgreSQL harness PASS results enable production runtime or close the separate JWT and final Step 13L review requirements.
