# Step 11X — Audit workflow integration foundation

## Scope and integration

Step 11X adds `IAuditEventWriter` and an unavailable default writer, then observes successful security-sensitive checkpoints in five Application workflows. The existing `AuditEventWriter` implements the interface and remains backed by an injected sink and server clock. No accepted sink is registered in production, no `AuditLog` row is written, and no database adapter is added. Existing constructors remain usable through an optional writer parameter that defaults to `UnavailableAuditEventWriter`. Synthetic security checks inject a bounded recording sink.

All requests use the internal `AuditWriteRequest.Create` path with validated workflow state or evidence. No public/chat caller supplies event type, audit ID, timestamp, or references. `AuditEventRecord` remains fixed metadata: exact user/conversation/project scope; optional typed action, tool, approval, execution, reconciliation, verification, or permission-grant IDs; bounded type/outcome; server UTC time. It has no prompt, chat text, AI output, tool output, exception, path, command, model name, key, arbitrary metadata, or raw payload field.

The exact observations are:

- `ApprovalWorkflowService`: after producing a granted action and approval evidence, emits `ApprovalGranted`/Success with exact action, tool, approval, and scope. After producing a rejected action, emits `ApprovalRejected`/Denied with action/tool/scope and **no invented approval ID**.
- `ToolExecutionPermissionValidator`: after a bounded decision, emits `PermissionAllowed`/Success or `PermissionDenied`/Denied with exact scope and tool. The grant ID is included only when a Found source grant has a nonempty ID and exact scope/tool/requirement binding. Audit failure cannot turn Allowed into Denied or Denied into Allowed.
- `TrustedToolExecutionService`: after validating a successful durable reservation, emits `ExecutionReserved`/Success with action/tool/approval/execution/scope. After a successful terminal durable completion and evidence/action creation, emits `ExecutionCompleted`/Success or `ExecutionFailed`/Failed with the **same execution ID**. Failed or uncertain reservation emits no reservation event; `ReconciliationRequired` after uncertain completion emits no false completion/failure event. Permission denial occurs before reservation and can emit only `PermissionDenied` through the permission validator.
- `ExecutionReconciliationService`: after issuing an assessment, emits `ExecutionReconciliationAssessed`/Inconclusive with action/execution/reconciliation IDs and known tool/approval/scope. `SafeToMarkFailed` remains advisory and does not become a Failed audit outcome or mutate state.
- `VerificationWorkflowService`: after issuing evidence, emits `VerificationAssessed` with exact action/execution/verification IDs and scope. Verified maps to Success, Failed to Failed, and Inconclusive to Inconclusive. Audit does not change verification evidence or outcome.

These are reached audit points, not durable event guarantees. A successful workflow call emits at most one event per reached point; the permission and execution observers can each emit one event in a single execution call. No audit retry or fallback occurs. A repeated business call can create another audit event; there is no deduplication claim.

## Failure and cancellation policy

The business/security workflow result is authoritative. `AuditObservation.RecordAsync` discards bounded audit failure statuses and catches non-cancellation audit exceptions, without changing approval, permission, handler invocation, execution state/outcome, reconciliation, or verification. The default writer returns Unavailable and never claims persistence. Audit failure does not roll back a completed in-memory or durable transition, and this step makes no atomic business-plus-audit claim.

Cancellation propagates before and after the audit boundary. If cancellation occurs while auditing after an approval transition, reservation, durable completion, reconciliation assessment, or verification evidence issuance, the caller may observe cancellation even though that business checkpoint already occurred. Cancellation while auditing `ExecutionReserved` can leave Reserved state with **no handler call**; future reconciliation is required. An event may have been accepted by a sink before cancellation is observed. No rollback or second audit attempt is made.

The writer and default unavailable path have no tool handler, dispatcher, database, verification, or chat dependency. Audit cannot grant permission, approve/reject differently, execute or retry a handler, alter durable completion, or mark verification successful. No real shell/Git/filesystem/browser/HTTP/MCP behavior or provider function call was added.

## Verification and production limits

Package-free Step 11X checks use synthetic workflows and a fixed-capacity recording sink. They cover exact event ordering and IDs, approval grant/rejection, permission allow/deny/revocation, execution reserve/terminal success/terminal failure, replay and uncertain completion, reconciliation and all verification outcomes, unavailable and throwing sinks, and cancellation during post-transition auditing. Existing checks remain included. Audit event validation still enforces fixed fields, event-family bindings, bounded messages (at most 80 characters), and no arbitrary text channel.

No production audit sink, `AuditLog` persistence, DbContext/EF/schema/migration/package change, outbox, queue, retry worker, hash chain, SIEM, chat/provider integration, or real PostgreSQL/OpenAI/Ollama call is added. Safe File Access remains disabled. Production sink durability, delivery, ordering, business-plus-audit atomicity, tamper resistance, PostgreSQL/pgvector behavior, semantic retrieval/ranking, and live providers remain unverified and deferred. This is not compliance logging or production audit readiness; a future transactional outbox/sink design is needed for reliable delivery.
