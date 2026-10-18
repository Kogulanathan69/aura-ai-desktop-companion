# Step 17F — reconciliation writer registration and live proof

**Result: incomplete; Reconciliation=false.** The existing `IExecutionReconciliationAssessmentWriter -> ExecutionReconciliationAssessmentPersistenceWriter` is a bounded, cancellation-aware, unregistered writer. It inserts an advisory `ExecutionReconciliationAssessments` row with exact action, scope, tool, approval, execution and observed version fields; it does not mutate `ActionExecutionStates`. Repeated assessments are separate historical rows. The workflow accepts this writer optionally and returns a bounded failure if it returns false.

The prior checkpoint changed the gated `--live-production-reconciliation-evidence` harness to call the real writer through `ExecutionReconciliationService` for both assessments, then independently read back the rows. Generated action IDs are part of exact cleanup, so a write followed by an ambiguous service failure can still be removed. Direct EF insertion remains only for invalid-row constraint tests. The current source review confirms that the writer is invoked in the real live path; it cannot establish that a live run persisted a row.

## Follow-up live evidence

**LIVE RESULT: OPERATOR-REPORTED PASS.** The operator manually ran the revised harness against a verified disposable PostgreSQL target and reports that credentials and environment variables were cleared afterward. This agent did not run the command or receive a target identifier, timestamp or Step 15B record. The exact sanitized output supplied was:

```text
DB CONNECTIVITY: PASS
SCHEMA CHECK: PASS
FIXTURE SETUP: PASS
VALID RESERVED ASSESSMENT: PASS
STALE VERSION: PASS
TERMINAL STATE: PASS
NEGATIVE BINDINGS: PASS
REPEATED ASSESSMENT: PASS
ASSESSMENT PERSISTENCE: PASS
CLEANUP: PASS
RESULT: PASS
```

The command was:

```text
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --live-production-reconciliation-evidence
```

The two gates must be exactly lowercase `true`, with `ConnectionStrings__DefaultConnection` naming an independently verified `aura_disposable_...` target. The source path confirms that the real `ExecutionReconciliationAssessmentPersistenceWriter` is supplied to `ExecutionReconciliationService` for both successful assessments; the harness re-reads those exact rows and verifies the durable state remains Reserved/version 1. The reported PASS therefore supports real writer execution, exact assessment persistence, the harness's negative-binding and stale-snapshot cases, distinct repeated assessment IDs, unchanged durable state and cleanup on the tested target. It does not identify that target for comparison with Step 15B, prove a concurrent-completion race, or independently attest how the core schema was established.

## Production decision

The harness's `STALE VERSION` case injects an internally inconsistent snapshot and proves service rejection of that shape. It does **not** race a real completion against the writer. The writer's insert is not transactionally version-coupled to the source's RepeatableRead read and does not lock or recheck `ActionExecutionStates`. A concurrent completion can occur between the read and insert, producing a historical assessment of a now-terminal execution. Step 14K requires stale-state and outcome handling under concurrency to be reviewed; Step 16F lists this gap before catalog approval. Current `ObservedVersion=1` records the source observation but does not enforce freshness at commit. One acceptable future policy could explicitly define these rows as historical observations that never authorize a current-state transition, with every consumer re-reading durable state; another is a serialized row-lock/version check before insertion. Neither policy is implemented and reviewed as a production contract. Changing the writer now would also require a fresh live run of the changed path, so this checkpoint does not infer a new guarantee from the existing PASS.

The real source checks the action, approval and ownership chain before issuing its snapshot. Separate foreign keys on the assessment row do not prove that chain independently, and the writer trusts the evidence object. The current live negative-binding cases exercise source and service boundaries; they do not inject a cross-row mismatch directly into the writer. Repeated assessments are intentionally allowed as distinct historical rows with distinct IDs; the table has no single-use index. A writer exception returns false without an execution-state mutation, and cancellation propagates, but an ambiguous commit can persist a row before a bounded failure reaches the service. The generated-ID cleanup in the live harness covers that possibility for its fixtures.

The exact future DI graph needs a compatible `IDbContextFactory<AuraDbContext>`, one scoped `IExecutionReconciliationSource -> ExecutionReconciliationPersistenceSource`, one scoped `IExecutionReconciliationAssessmentWriter -> ExecutionReconciliationAssessmentPersistenceWriter`, a scoped `IExecutionReconciliationService` supplied with the writer, a reviewed scope validator and policy, and no duplicate/factory substitute registrations. The Step 14K inspector currently checks only the source descriptor and its exact `ReviewedReconciliation` catalog entry. Approving that entry alone would not attest that the real writer is actually wired into the service. This remains a composition-review gap, not a reason to weaken the inspector.

Step 15B supplies a broader read-only catalog verifier, but its checked-in review records no target-bound PASS and no historical core-schema provenance approval. The operator's Step 17F output does not identify the target, so same-target applicability cannot be established. This is a separate blocker from the writer PASS. The stale-write policy and full DI graph are also unresolved. No writer/source/service registration or reviewed catalog entry was added. `Reconciliation=false`, all other composition fields remain false, all production flags remain false, and activation remains denied.
