# Step 11W — Audit trail and security-event foundation

> Step 11X later integrated bounded audit observations into the approval,
> permission, execution, reconciliation, and verification workflows. The
> no-workflow-integration statements below describe the historical Step 11W state;
> see `STEP-11X-SECURITY.md` for current behavior.

## Scope and relationship to existing AuditLog

This step adds a bounded Application-layer event model, validation policy, explicit writer, and sink interface. No action, approval, permission, execution, reconciliation, or verification workflow calls the writer automatically. The supplied sink returns Unavailable; it neither persists nor claims to persist an event. No production sink or database adapter is registered. The existing domain `AuditLog` has free-text `Description`, `OldValue`, `NewValue`, `EntityType`, and string status fields, so mapping the bounded model to that entity requires a separate reviewed adapter. Its entity, EF configuration, and schema are unchanged. This foundation is not production audit logging, tamper resistance, immutable database history, or complete compliance logging.

Audit is metadata, not authorization, approval, execution, verification, or reconciliation. Audit failure cannot grant permission, approve an action, run or retry a handler, alter a durable outcome, or mark verification successful because the writer has only a sink and clock dependency and is not wired into those workflows.

## Event shape and validation

`AuditEventIdentifier` is a server-generated Guid with a private constructor and internal minting factory. `AuditWriteRequest` also has no public constructor or minting factory; future trusted workflow integration can use its internal creation path. The writer obtains UTC time from `IDateTimeProvider`; callers cannot submit event ID or time. `AuditEventRecord` has only get-only fields: ID, bounded type and outcome, exact `ActionScope` (UserId, ConversationId, ProjectId), optional ActionId, bounded ToolId, ApprovalId, ExecutionId, VerificationId, ReconciliationId, PermissionGrantId, and server UTC `OccurredAt`. It has no string, description, prompt, chat content, AI response, command, argument, path, file content, stdout/stderr, exception, stack, DB error, JWT, role, secret, arbitrary JSON, dictionary, or metadata field.

Event types are `ActionProposed`, `ApprovalRequested`, `ApprovalGranted`, `ApprovalRejected`, `PermissionAllowed`, `PermissionDenied`, `ExecutionReserved`, `ExecutionCompleted`, `ExecutionFailed`, `ExecutionReconciliationAssessed`, `VerificationAssessed`, and `SecurityDenied`. Outcomes are `Success`, `Denied`, `Failed`, and `Inconclusive`. Unknown enum values, empty scope components, empty optional Guid references, or an invalid event/reference combination fail closed before reaching the sink.

The conservative event-family rules are:

- `ActionProposed` and `ApprovalRequested`: Success; ActionId and ToolId.
- `ApprovalGranted`: Success; ActionId, ToolId, and ApprovalId. `ApprovalRejected`: Denied; ActionId and ToolId without an approval ID.
- `PermissionAllowed`: Success; ToolId and PermissionGrantId. `PermissionDenied`: Denied; ToolId, with optional PermissionGrantId.
- `ExecutionReserved` and `ExecutionCompleted`: Success; ActionId, ToolId, ApprovalId, and ExecutionId. `ExecutionFailed`: Failed with those same references.
- `ExecutionReconciliationAssessed`: Inconclusive; ActionId, ExecutionId, and ReconciliationId. `VerificationAssessed`: Success, Failed, or Inconclusive; ActionId, ExecutionId, and VerificationId. These two families may also carry ToolId and ApprovalId if known.
- `SecurityDenied`: Denied, scoped to the exact user/conversation/project; ActionId, ToolId, ApprovalId, and ExecutionId may be present when known.

Unlisted reference types are rejected for each family. These are event-shape rules, not independent proof that the referenced security action occurred; future workflow integration must supply trusted values. The record is immutable through its supported API, but this does not establish durable or tamper-resistant storage.

## Sink, errors, cancellation, and limits

`IAuditEventSink.WriteAsync` returns `Accepted`, `Unavailable`, or `Failed`. `UnavailableAuditEventSink` always returns Unavailable, so the default path never reports a persisted event. A synthetic sink in security checks can accept an event; `Success` means only that the sink accepted it, not that a database committed it. The writer retries nothing and has no fallback. Cancellation propagates before and after the sink call; a sink may have accepted an event before cancellation is observed, with persistence semantics deferred to a future adapter. Non-cancellation sink exceptions map to fixed Failed without raw error text. Result messages are at most 80 characters.

The model uses only fixed Guids, bounded enums, UTC DateTime, exact `ActionScope`, and the existing ToolIdentifier with its 64-character input limit. There are no arbitrary strings or unbounded collections. Package-free security checks cover every event family, required and forbidden references, invalid enums, scope and empty-ID rejection, private issuance, immutable shape, default unavailable sink, safe failures, cancellation, and unchanged chat/file-access surfaces.

No AuditLog write, DbContext/EF/schema/migration/package change, real handler, shell/Git/filesystem/browser/HTTP/MCP execution, chat/provider integration, or real PostgreSQL/OpenAI/Ollama call is added. AiChatRequest remains Prompt-only and Safe File Access remains disabled. Production event provenance, sink durability and ordering, failure handling across workflows, PostgreSQL/pgvector runtime, semantic retrieval/ranking, and live AI providers remain unverified and deferred.
