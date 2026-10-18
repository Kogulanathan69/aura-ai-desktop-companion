# Step 17N — trusted target and verification schema foundation

## Purpose and scope

This step carries the project-only identity proposed for `owned-project-metadata-read` through the existing in-process approval and reservation path, and adds a **pending migration source** for conservative verification assessment version binding. It adds no real handler, allowing policy, independent proof adapter, production registration or runtime enablement. The Step 17M candidate remains a candidate; this step does not prove that a read-only metadata result can be independently verified.

## Project target and approval binding

`OwnedProjectTarget` is a sealed immutable value containing only a nonempty `ProjectId`. It has no public constructor, user override, tool override, permission/capability field, path, URL, command, JSON or secret. Its internal issuer requires an approved `ActionDescriptor` and matching in-process `ApprovalEvidence`, plus a Reserved/version-1 `ActionExecutionState` with nonempty execution ID and exact action, user/conversation/project scope, tool and approval bindings. The target project is **exactly** `action.Scope.ProjectId`; a different project, foreign scope, action, approval or tool cannot be substituted.

`ActionDescriptor.Scope.ProjectId` is set in the server-resolved action scope. `ApprovalEvidence` captures that scope and `ApprovalValidation.IsBoundToApprovedAction` binds it to the approved action ID/tool/approval ID. The execution state's existing `ProjectId`/scope participates in reservation and conditional transition predicates; no duplicate target column is needed for this project-only identity. This is an **in-process structural binding**, not proof that the stored `AIAction` and `ActionApproval` form a verified cross-entity chain. Step 17L's store pre-insert chain/lifecycle gap and the lack of a durable approval issuance source remain blockers. No caller-supplied project ID is accepted by `ExecutionRequest`, and target issuance never reads one from the handler request.

## Trusted request propagation

`ToolInvocationRequest` is now a sealed class with get-only `ToolId` and optional get-only `OwnedProjectTarget`. Its public constructor creates a **targetless** request for the existing disabled/test boundary; it is not authority. Only the internal `FromReserved` factory can attach a target. `TrustedToolExecutionService` calls it **after** validating the successful reservation's exact action/scope/tool/approval/execution ID/version and before its single handler call. If target issuance or project equality fails after reservation, it returns `ReconciliationRequired`, not a fallback target. Existing ownership, approval, permission, policy, one-call, no-retry and durable completion gates remain. No production handler is registered, so no production tool executes.

The target object cannot mutate after reservation. A future handler must still independently validate active ownership and exact resource access; possession of `OwnedProjectTarget` is not an authorization grant. For any metadata key, expected value or revision beyond project identity, action/approval binding would need a separate reviewed model and likely schema change. None was added here.

## Verification proof and execution version

`VerificationProofSource` is bounded to `None=0` and reserved `OwnedProjectMetadata=1`. Proposed metadata proof version is exactly `1`; this step has **no independent proof adapter**, observation/digest or policy that can claim it. The existing `ExecutionEvidenceInput` remains an incomplete production proof contract and has no independently attested execution version. A future adapter must retrieve the exact terminal durable state, require version 2, bind action/user/conversation/project/tool/approval/execution IDs, independently re-observe a meaningful metadata result, enforce source/version/freshness, and reject stale or foreign observations. Handler success alone remains Inconclusive under `ConservativeVerificationPolicy`.

The `ExecutionVerificationAssessments` model gains nullable `ExecutionVersion` (`bigint`). Null preserves historical advisory rows created under the previous constraint. The pending migration replaces only `CK_ExecutionVerificationAssessments_State`: old null-version rows remain restricted to `ProofSource=None`, `ProofVersion=0`, Failed/Inconclusive; version-2 rows may use None/0 or the reserved OwnedProjectMetadata/1 pair, again **only Failed/Inconclusive**. **Verified remains forbidden.** No default creates a fabricated version or proof. The migration adds no index, seed, data repair, delete or unrelated table mutation. `Down` restores the old check and removes the column; any future version-2 source-1 rows would need an explicit retention/rollback decision before Down. The migration was written explicitly because this repository has no EF model snapshot; its Up/Down operations are checked offline, but no generated snapshot comparison or live application result is claimed. This migration is source only and was **not applied**. The Step 15B/17K live baseline covers the prior five applied migrations, not this pending sixth migration; it must be separately reviewed and reverified before production persistence use.

## Assessment writer

`IExecutionVerificationAssessmentWriter` and `ExecutionVerificationAssessmentPersistenceWriter` are unregistered. The scoped-future writer uses a fresh context from `IDbContextFactory<AuraDbContext>`, accepts only immutable in-process `VerificationEvidence` with Failed/Inconclusive outcome and exact terminal execution version 2, reads an exact matching action/scope/tool/approval/execution state, checks active user and action/approval/project/conversation chain, and inserts one bounded assessment with `ProofSource=None`, `ProofVersion=0`. It does not write a Verified row, mutate execution state, retry, log raw exceptions or access a handler. Cancellation propagates; other persistence failure returns false. Distinct assessment IDs permit repeated advisory assessments. A saved row does not imply independent proof or current-state freshness. The factory is absent from normal API DI and this writer is not registered until migration and live evidence are reviewed.

## Readiness and remaining work

`Verification=false`, `ToolHandlers=false`, `ExecutionPolicy=false`, `TrustedExecution=false`; all other composition fields and `CompositionComplete` remain false. `ProductionAdaptersEnabled`, `ToolExecutionEnabled`, `AuditPersistenceEnabled`, `SafeFileAccessEnabled` and `CloudAiEnabled` remain false. `UnavailableRuntimeCapabilityReadiness` and the unavailable final approval source continue to deny activation. No handler, allowing policy, trusted executor, worker, polling, retry, auto migration or `EnsureCreated` was added to normal startup.

Remaining blockers: approve and apply the new migration only through a separate controlled process after baseline review; obtain live schema/persistence evidence; close the execution store's cross-entity approval chain and ambiguous-commit review; establish durable approval provenance; define a meaningful independently verifiable metadata observation and versioned proof model; implement a real evidence source and policy, while keeping wrong/stale proof from `Verified`; review the writer's DI graph and historical-row policy; then implement and separately review the handler, allowing policy, registry/catalog, and final activation prerequisites. The existing Step 15B verifier intentionally still expects the five previously applied migrations and must be revised before it can attest a target after applying this pending migration.

```text
dotnet build Aura.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false
git diff --check
git status --short
git diff --stat
```
