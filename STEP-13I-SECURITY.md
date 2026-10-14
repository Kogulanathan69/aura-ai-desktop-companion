# Step 13I — controlled audit outbox live verification

This adds a test-only command to `security-checks` for the unregistered Step 13H dispatcher. Normal `dotnet run --project security-checks` remains an offline suite. The command neither runs nor connects unless both exact opt-in environment flags equal `true`. It then requires a separate connection string with an explicit host, username, and a database name beginning `aura_disposable_`. Use a dedicated, disposable PostgreSQL database with pgvector available. The name and flags are guardrails, not proof that a database is safe to destroy; the operator must verify the target independently. No credentials are stored in the repository.

In PowerShell, after creating and independently checking a disposable test database, set these environment variables for the current process:

```powershell
$env:AURA_LIVE_AUDIT_OUTBOX_VERIFY = 'true'
$env:AURA_AUDIT_OUTBOX_DB_IS_DISPOSABLE = 'true'
$env:AURA_AUDIT_OUTBOX_DISPOSABLE_CONNECTION = '<PostgreSQL connection string for an existing aura_disposable_... database>'
dotnet run --project security-checks -- --live-audit-outbox-verification
```

The repository has no historical baseline migration for its core tables. The command therefore **refuses an empty database**. Before the command can run, a separately reviewed disposable baseline must already contain the core tables and applied migration IDs through Step 13J. As of Step 13K, this command does not apply migrations; the separate `--live-audit-outbox-hardening` command controls Step 13J migration application. It checks the outbox and event tables, inserts unique ownership/action/approval/execution fixtures, then removes only those generated IDs. If no reviewed baseline is available, this is a live-verification blocker; do not fabricate migration history or weaken constraints to make the command pass.

The command checks successful delivery and exact typed audit binding; explicit Failed and Unavailable sinks; exact and mismatched duplicate event IDs; two concurrent dispatchers; cancellation before access and inside a controlled sink; malformed event outcome; and each execution-state binding field. A failed cleanup fails the run. The command does not create a database, scan outbox rows, run a hosted worker, retry, or call a tool or AI provider.

This PC has no PostgreSQL. The live command, migrations, and database connectivity were **not** run here. Offline checks and compilation cannot establish PostgreSQL lock behavior, migration compatibility with a real database, transaction timing, or cleanup success. A future operator should inspect the disposable database target and run this command before interpreting any pass as evidence. Even a passing run does not establish exactly-once delivery. An external sink can complete a side effect before a cancellation or crash rolls back the outbox transaction, leaving Pending for a later explicit attempt. Duplicate external side effects remain possible.

At Step 13I, the Step 13G database check constraint allowed a manually written Failed row with nonnull `DeliveredAt`; the Step 13H dispatcher itself writes null for Failed. Step 13J adds a separate migration to close that gap; until applied, the old database constraint remains in effect. The production API still does not register the dispatcher or verifier. AuditSink and ExecutionState runtime readiness, production adapters, tool execution, and Safe File Access remain disabled.
