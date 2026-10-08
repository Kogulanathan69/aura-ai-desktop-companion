# Step 11R — Trusted tool execution workflow foundation

## Scope and architecture

Application owns `ExecutionRequest`, `ExecutionIdentifier`, `ExecutionOutcome`,
`TrustedExecutionContext`, immutable `ExecutionEvidence`, `ExecutionOperationResult`,
`IToolExecutionScopeValidator`, `IToolExecutionPermissionValidator`,
`IToolExecutionPolicy` and `TrustedToolExecutionService`. The Step 11N registry
resolves one server-registered handler. Step 11O lifecycle receives narrow internal
begin/complete methods, and Step 11P evidence must match the exact Approved action.
Step 11Q can consume a structural `ExecutionEvidenceInput` projection later; this
step does not call the verification workflow or install an evidence source.

The existing `ToolExecution`, `AIAction`, `Verification`, `Permission` and `AuditLog`
entities were inspected. None is constructed or persisted. New evidence is an
in-process application value, not a database row. No endpoint, DI registration,
real handler, provider change or infrastructure adapter is added. Application has
no Infrastructure dependency. The only invoked handler is a synthetic test fixture.

## Request and exact preconditions

The internal `ExecutionRequest` contains only an existing `ActionIdentifier`. It
cannot choose execution ID, tool, approval, permission, policy, arguments, commands,
paths, URLs, script, output or payload. `ActionDescriptor`, `ApprovalEvidence` and
`ActionScope` are supplied separately as server-known application values, never
through chat or public request DTOs. There is no public execution endpoint.

Before calling a handler, `TrustedToolExecutionService.ExecuteAsync`:

1. Checks cancellation, nonempty user/conversation/project scope, non-null action
   and request, and exact requested action ID.
2. Requires exact action/scope equality, `Approved` status and no prior execution
   link on that descriptor.
3. Requires actual immutable `ApprovalEvidence` linked to the action's approval ID
   and exact action/scope/tool via `ApprovalValidation`. ID alone is insufficient.
4. Freshly checks ownership through `IToolExecutionScopeValidator`.
5. Resolves exactly one server-registered descriptor/handler for `action.ToolId`.
6. Requires `IToolExecutionPermissionValidator` to validate the required permission
   for that exact scope/tool. The supplied validator denies all. A future adapter
   must authenticate the user, validate owned conversation/project and exact
   non-revoked permission; metadata does not grant permission.
7. Requires the separate `IToolExecutionPolicy` to allow that registered tool. The
   supplied policy denies all. The test policy allows one exact identifier only.
   This is feature enablement, not permission or approval.
8. Revalidates ownership immediately before the handler, checks cancellation and
   a non-default UTC server start time no earlier than action update time.

All gates are required. Approval alone, ownership alone, registration, AI text,
summary or tool permission metadata cannot authorize execution. Unknown tools,
invalid states, mismatched/foreign approval, foreign scope, missing permission and
policy denial fail closed before the handler. Current `ToolDescriptor.Enabled` stays
false. The separate Step 11N `DisabledToolDispatcher` is unchanged and still denies
every tool. No production policy/permission/ownership adapter is registered.

## Handler call, lifecycle and safe outcomes

After validation, the coordinator mints an internal `TrustedExecutionContext` with
server execution ID and UTC start time. Its narrow `ActionDescriptor.BeginExecution`
requires exact action/scope/tool/approval binding and creates a new Executing value.
This value is local until the handler returns; no storage write occurs. Direct
`ActionLifecycleService.TransitionAsync` still denies Approved -> Executing and
Executing -> Succeeded/Failed. No public constructor/setter can make the linked
execution action.

The coordinator invokes the single resolved `IToolHandler.ExecuteAsync` once with
`ToolInvocationRequest(action.ToolId)`, which has no arguments. There is no retry,
fallback or second handler call. Existing handler results are fixed status/message
values, not raw output. Only `ToolExecutionStatus.Success` maps to execution
Succeeded; all other or invalid/null statuses map to Failed. A thrown non-cancellation
exception maps to a fixed safe Failed completion. Handler cancellation propagates.
The coordinator checks cancellation after the handler and will not fabricate a
completed result/evidence for a cancelled call.

`ExecutionEvidence` is issued after an eligible handler completion with a new
server-generated ID, exact action ID, user/conversation/project scope, tool ID,
approval ID, Succeeded/Failed outcome and UTC started/completed timestamps. The
completed action is a new Succeeded or Failed value linked by `ToolExecutionId`.
Approval ID and creation time are preserved. `VerificationId` remains null and
`IsVerified` remains false. Execution success is not verification.

Evidence/identifier constructors are private, properties get-only and issuance
internal to Application. No supported public constructor/JSON deserialization
factory can fabricate them. `MatchesBinding` rejects another action, scope, tool
or approval. This protects supported external APIs, not malicious reflection or
compromised trusted application code. The evidence contains no stdout, stderr,
exception, OS error, stack trace, path, file content, secret, token or arbitrary text.
`ExecutionEvidence.ToVerificationInput()` creates only the bounded Step 11Q DTO
with exact matching IDs, outcome and completion time. It does not persist it,
install a trusted `IExecutionEvidenceSource`, run verification or make the result
independent proof. A future source must attest provenance/current state before
Step 11Q treats that DTO as trusted execution input.

## Bounds, cancellation and persistence limitations

- Request: one server-known 128-bit action ID, zero arguments/collections/text.
- Execution ID, action/approval IDs and three scope IDs: fixed Guids.
- Tool ID: existing 64-character original-input limit.
- Evidence: two UTC `DateTime` values and one two-value outcome in addition to
  the fixed identifiers; no free-form payload.
- Fixed result messages: at most 80 characters, never handler or exception text.

Denied results contain no action/evidence. Public `Denied` cannot fabricate
Success. Non-cancellation dependency errors become fixed Failed responses without
raw details. All async scope/permission/policy/handler contracts accept a token;
the coordinator checks it before and after each awaited boundary. No automatic
retry or fallback occurs.

Cancellation after a future handler performs side effects can leave effects with
no completion evidence or stored state. Production reconciliation and durable
outbox/version handling are deferred. A retained immutable Approved action may be
executed again in a later call, producing a different execution ID. Only at most
one handler invocation **per service call** is guaranteed. There is no durable
exactly-once/single-use, atomic ownership/permission check, replay prevention,
approval expiry or concurrency control. No `ToolExecution`, `AIAction`,
`ActionApproval`, `Verification`, `AuditLog` or `Permission` row is written.

## Non-goals and verification

No Process/shell/PowerShell/cmd/bash/Git/filesystem/browser/HTTP/MCP/OS execution,
real tool handler or adapter, Safe File Access enablement, provider tool calling,
automatic AI-triggered execution, RAG integration or chat/router change is added.
Safe File Access remains disabled by default. `AiChatRequest` remains Prompt-only.
No real PostgreSQL/OpenAI/Ollama calls, package additions, schema changes or
migrations. PostgreSQL/pgvector and live AI runtime remain deferred.

`ExecutionChecks` adds 65 package-free checks with synthetic handlers, scope,
permission and policy fakes. They cover exact bindings, default denial, distinct
permission/policy gates, one handler call, no retry/fallback, safe outcomes and
exceptions, cancellation before/through/after handler, lifecycle restrictions,
immutable evidence, Step 11Q structural handoff, replay limitation, bounded
results, unchanged chat/router/file access and no domain persistence dependencies.
All prior checks remain included. The suite's pre-existing isolated filesystem
fixtures remain unchanged; the new execution checks perform no real OS/network/
database operations. One initial new test used the same action ID in two states
for a mismatch; it was corrected to use a different action ID before final pass.

User-local .NET SDK `10.0.401` was verified. `dotnet build Aura.slnx --no-restore`
passed with zero warnings/errors. The full package-free suite
`dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore`
passed all 794 checks (729 prior plus 65 new). `git diff --check` and separate
untracked-file whitespace checks passed. Git status, diff/stat, exact untracked
file list and changed source were inspected.

A separate review-only pass checked exact approval binding, ownership/permission/
policy gates, handler call count, lifecycle changes, safe cancellation, replay and
exactly-once claims, evidence, no automatic verification, Clean Architecture,
no persistence/real OS/chat/provider changes and no unrelated changes. No remaining
Critical/High/Medium/Low implementation defects were identified. Production
ownership/permission/policy configuration, real handler behavior, durable replay/
reconciliation, verification provenance and runtime DB/provider integration remain
unverified. Suitable for separate human review before commit; this is not production
execution readiness. No commit, push or PR was created.
