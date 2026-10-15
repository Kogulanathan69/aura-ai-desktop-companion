# Step 14L — production verification composition readiness

## Purpose and scope

**Verification: false. CompositionComplete: false — NOT READY.** This step tightens an observational composition check. It does not register an evidence source, add a worker, apply migrations, run a tool, or enable production execution. Normal `Aura.Api/Program.cs` retains `UnavailableRuntimeCapabilityReadiness`.

## Current verification foundation and truth

`IExecutionEvidenceSource` is a bounded future adapter contract. Its only concrete implementation in production assemblies is `UnavailableExecutionEvidenceSource`, which returns null and propagates cancellation. Normal API startup registers no implementation. There is no independent trusted evidence provider or production proof policy; `ConservativeVerificationPolicy` returns Failed for a failed execution report and Inconclusive otherwise. It never derives Verified from a success report.

`VerificationWorkflowService` validates action scope, approved context, approval binding and tool identity, then asks the injected evidence source for an exact execution ID. It denies missing or malformed evidence, checks action/scope/tool/approval/execution IDs and UTC completion time, and rechecks ownership after policy assessment. It rejects a Verified outcome for a failed or unknown execution report. An independent policy must establish proof; a successful handler report or durable execution status cannot verify itself. The workflow's evidence is advisory and does not mutate execution state.

The current `ExecutionEvidenceInput` contract does **not** carry an execution-state version or proof provenance. Therefore exact version binding and stale-proof rejection are not demonstrated by a production adapter. The workflow checks the IDs and timestamps available in the contract, but there is no independent source to attest their freshness. These gaps remain blockers; this step does not invent evidence or a version.

`ExecutionVerificationAssessments` is a schema foundation with exact action/user/conversation/project/tool/approval/execution identifiers and relationships to durable action state. Its constraint accepts only Failed or Inconclusive, with `ProofSource=None` and `ProofVersion=0`; it cannot store a durable Verified claim. No assessment writer exists. The table, migration, offline fixtures and disposable live database evidence do not establish independent proof or production readiness. Separate Step 13E/13F checks cover the schema and binding limits.

ExecutionState, Reconciliation, AuditSink and AuditDelivery are independent capabilities. Reconciliation is an advisory assessment of durable state, while audit records an observation or delivery; neither is an independent verification source. Configuration flags cannot replace evidence provenance.

## Fail-closed composition rule and changes

The inspector now requires exactly one scoped `IExecutionEvidenceSource` descriptor with a concrete type in the source-controlled reviewed verification catalog. It explicitly excludes `UnavailableExecutionEvidenceSource`. The catalog is empty because **no eligible independent implementation exists**; no arbitrary type is treated as reviewed. Factory, instance, duplicate, singleton, transient, unavailable and synthetic sources cannot pass. A future catalog entry requires separate review of exact implementation identity, production DI/lifetime, genuine independence, version/provenance and failure semantics. Catalog membership without matching DI is insufficient, as is registration without review.

Step 14L changes only the inspector check, adds focused offline checks and this document. The inspector reads service descriptors only and performs no DB, network, filesystem, provider or tool operation. There is no new hosted service, poller, retry loop, timer, scheduler or automatic verification.

| Composition field | Current value |
| --- | --- |
| Ownership | false |
| Permission | false |
| ExecutionState | false |
| AuditSink | false |
| AuditDelivery | false |
| Reconciliation | false |
| Verification | false |
| ExecutionPolicy | false |
| ToolHandlers | false |
| TrustedExecution | false |
| **CompositionComplete** | **false — NOT READY** |

`ProductionAdaptersEnabled`, `ToolExecutionEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` retain false defaults.

## Remaining blockers and validation

Before Verification can be reviewed as ready, implement and assess a genuinely independent, scope-bound source and proof policy; bind evidence to exact execution and version with stale/malformed/missing evidence denial; define durable assessment persistence and idempotency; review migration baseline and live PostgreSQL behavior; and intentionally review normal-startup DI and lifetime. Ownership, permission, durable state, audit, reconciliation, policy, handlers, trusted composition and Step 13L final security approval remain separate gates. A disposable live harness PASS alone cannot clear this gate.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
```
