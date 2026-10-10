# Step 12J — Reconciliation and verification persistence readiness

Step 12J compares the bounded Step 11U reconciliation assessment and Step 11Q verification evidence with the current EF model. Neither can be persisted with exact bindings and trusted provenance on the existing schema. No production source or proof adapter was implemented or registered. Startup and informational readiness remain false for both capabilities.

## Reconciliation assessment

`ExecutionReconciliationSnapshot` requires current `ActionExecutionState` plus exact nonempty action, user, conversation, project, tool, approval, and execution IDs, state version, and a bounded reason. The service accepts only a valid Reserved state and issues immutable evidence with a new reconciliation ID, observed status/version, advisory disposition, and server UTC issuance time. Even `SafeToMarkFailed` is advice; it performs no state transition.

There is no reconciliation entity, table, DbSet, or EF configuration. The existing `ToolExecution` and `AIAction` rows lack the exact execution-state version and bindings identified in Step 12H. They cannot attest a current snapshot or durably record a reconciliation decision. A future read adapter must first enforce ownership, read a single consistent current state and reason from a trusted source, and reject missing, stale, conflicted, or malformed bindings. A future persistence design must define whether assessments are retained, their uniqueness/idempotency identity, their relationship to the observed version, and how a repeated or concurrent assessment is handled. A stored advisory decision must never be treated as authorization to retry a handler or mutate execution state. No recovery action is part of this step.

## Verification evidence

`VerificationEvidence` binds verification ID, action and full scope, tool, approval, execution ID, outcome, and server UTC issuance time. The input must be a trusted completed execution with matching bindings and a UTC completion time after approval. `Verified` additionally requires independent tool-specific proof; a successful execution report alone cannot establish it. The current conservative policy returns `Failed` for a failed report and `Inconclusive` otherwise.

The generic `Verifications` table has a GUID row ID, required `AIActionId`, optional `ToolExecutionId`, free-form verification type/status, three text fields, and timestamps. It lacks required user/conversation/project scope, typed tool/approval/execution IDs, constrained outcome, evidence issuance time, and proof provenance. `ToolExecutionId` is nullable, has no unique execution-assessment constraint, and is not the trusted execution attempt ID. The table's result text and details must not be used to pack missing bindings, proof, tool output, or sensitive content. Its action cascade delete and execution SetNull relationship can erase or detach historical evidence. It cannot substantiate a durable `Verified` claim.

A future design needs a separately reviewed bounded evidence schema and trusted execution source. It must bind exact scope and IDs to a completed authorized attempt, define independent proof type/provenance and validity rules without arbitrary payloads, constrain outcome and UTC times, and establish transactional or versioned checks against the execution state where necessary. It must specify deduplication, concurrent assessment, staleness, retention, and deletion behavior before any persisted verification claim is trusted. Database persistence alone does not make proof independent.

## Runtime and checks

`UnavailableExecutionReconciliationSource` and `UnavailableExecutionEvidenceSource` remain the only source implementations; `ConservativeVerificationPolicy` remains the only production policy. Normal `Aura.Api/Program.cs` uses `UnavailableRuntimeCapabilityReadiness`; `PersistenceCapabilityReadiness` also reports `Reconciliation=false` and `Verification=false`. `ProductionAdaptersEnabled`, `ToolExecutionEnabled`, and `SafeFileAccessEnabled` default to false. No handler, tool call, OpenAI/Ollama call, database write, migration, entity, DbSet, EF configuration, package, or runtime activation was added.

Package-free Step 12J checks inspect EF metadata, source and policy implementations, unavailable-source cancellation, and disabled readiness. Existing Step 11U/11Q checks cover service binding and conservative outcomes. These checks do not establish live PostgreSQL consistency or independent proof. Any future adapter and schema need a separate review and live database verification before readiness can be enabled.

With .NET 10.0.302, `dotnet build Aura.slnx --no-restore` passed with no warnings or errors, and the full offline security suite passed **1,261 checks**. The security-check project emitted two pre-existing nullable warnings. No live database verification was run.
