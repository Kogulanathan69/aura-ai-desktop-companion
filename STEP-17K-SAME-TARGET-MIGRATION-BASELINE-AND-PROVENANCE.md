# Step 17K — same-target migration baseline and provenance

## Purpose and evidence status

Review the read-only Step 15B verifier on the same independently verified disposable PostgreSQL database used for the operator-reported Step 17A execution-state and Step 17F reconciliation-writer runs. **LIVE RESULT: OPERATOR-REPORTED PASS.** The operator manually verified target continuity and supplied these non-credential identifiers: `database_name: aura_disposable_test`, `database_oid: 24576`, and `system_identifier: 7696813047573034172`. The operator reports that credentials and connection-string environment variables were cleared afterward. This agent did not connect to PostgreSQL. The earlier Step 17A/17F records did not include these identifiers, so target continuity is **operator-attested**, not independently cross-matched against identifiers captured in those earlier records.

The exact sanitized output supplied for this run was:

```text
DB CONNECTIVITY: PASS
MIGRATION HISTORY: PASS
MIGRATION BASELINE: PASS
CORE TABLES: PASS
KEY CONSTRAINTS: PASS
SECURITY SCHEMA: PASS
RESULT: PASS
```

## Exact read-only command

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-migration-baseline-evidence
```

Set `AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE=true`, `AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE=true`, and a process-only `ConnectionStrings__DefaultConnection` for the verified target. The first two must be exactly lowercase `true`. Use a read-only role, enter its password securely, and clear all three variables afterward using the Step 15B PowerShell procedure. Do not put the connection string, password, token, raw catalog rows, or raw database exceptions in evidence. Capture command, commit, time, a non-secret target continuity identifier from an approved inventory record, the six bounded categories, and `RESULT`.

The expected successful categories are DB CONNECTIVITY, MIGRATION HISTORY, MIGRATION BASELINE, CORE TABLES, KEY CONSTRAINTS, SECURITY SCHEMA, and RESULT. SKIPPED and FAIL are not PASS. The harness performs catalog/history SELECT queries only; it does not apply migrations, create schema, repair rows, or write fixtures.

## Repository migration and core-schema provenance

The checked-in targeted migrations are exactly `20261014000000_AddToolPermissionGrants`, `20261014000001_AddActionExecutionStates`, `20261014000002_AddSecurityPersistenceBundle`, `20261014000003_AddSecurityAuditOutbox`, and latest `20261014000004_HardenSecurityAuditOutboxDelivery`. Step 15B requires the database history to contain exactly those IDs and compares their lexical order. `__EFMigrationsHistory` has no application timestamp, so this does not prove the chronological deployment sequence.

The verifier checks selected critical columns, primary keys, foreign keys, unique indexes and validated checks on Users, Projects, Conversations, ToolPermissionGrants, AIActions, ActionApprovals, ActionExecutionStates, ExecutionReconciliationAssessments, ExecutionVerificationAssessments, SecurityAuditEvents and SecurityAuditOutbox. Its current-catalog PASS would support the tested target's **present shape** in those bounded areas. It is not a complete schema audit and cannot by itself prove how core tables were created.

**Core-schema provenance conclusion: B — partially unresolved.** The reported same-target PASS establishes the verifier's bounded current catalog and exact five targeted migration IDs on that target. The repository has EF configurations for core entities but no historical migration creating the pre-existing Users, Projects, Conversations, AIActions and ActionApprovals tables, no EF model snapshot, and no reviewed external baseline DDL/deployment record tied to the target. The exact missing artifact is an approved, reproducible source and application record for that pre-targeted core schema, including its version, creation order and relationship to the first checked-in targeted migration. The current catalog and five targeted migrations cannot reconstruct that history. No migration is added or applied by Step 17K.

## Readiness impact of the operator-reported same-target PASS

| Path | What a same-target PASS could close | What remains |
| --- | --- | --- |
| Step 17A ExecutionState | The bounded **current execution-state schema and targeted migration-history** blocker is closed for the operator-attested target: table, selected columns/FKs/checks and unique approval/execution indexes passed | Historical core provenance, cross-entity approval/ownership integrity, ambiguous-commit recovery, factory/store DI and catalog review |
| Step 17F Reconciliation | The bounded **current reconciliation schema and targeted migration-history** blocker is closed for the operator-attested target: assessment table and selected columns/keys/checks passed | Historical core provenance, stale-assessment policy, full factory/source/writer/service DI, writer-aware composition review |
| AuditSink / AuditDelivery | The bounded **current audit schema and targeted migration-history** blocker is closed for the operator-attested target: audit event/outbox tables, selected keys/checks and latest hardening migration ID passed | Historical core provenance, live sink/delivery evidence, retention/replay/idempotency policy, DI and catalogs |
| Ownership / Permission | The bounded **current ownership/grant schema** blocker is closed for the operator-attested target: selected Users/Projects/Conversations/grant columns, keys, FKs and grant constraints passed | Historical core provenance, real JWT and foreign-row isolation, revoked-grant denial, grant lifecycle policy, exact DI and catalogs |

This operator-reported same-target PASS closes only the verifier's bounded **current-catalog and targeted-history** portion of these paths. It does not prove full physical-schema equivalence, migration application chronology, original core-schema creation, data integrity of every row, or the separate live/DI/policy requirements. The source of continuity is the operator's manual verification; previous run records did not contain matching identifiers. No production-composition field changes solely from this verifier. All ten composition fields and `CompositionComplete` remain false. ProductionAdaptersEnabled, ToolExecutionEnabled, AuditPersistenceEnabled, SafeFileAccessEnabled and CloudAiEnabled remain false; the final approval source denies activation.

## Validation

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
git status --short
git diff --stat
```
