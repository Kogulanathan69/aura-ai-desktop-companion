# Step 11P — Approval workflow foundation

## Scope and architecture

Application owns ApprovalRequest/Decision, ApprovalIdentifier, immutable
ApprovalEvidence, ApprovalValidation, fixed ApprovalOperationResult/statuses,
IActionApprovalScopeValidator and IApprovalWorkflowService/ApprovalWorkflowService.
There are no API endpoints, DI registrations, production ownership adapters,
approval UI, database stores, enabled tools or provider integrations.

Existing ActionApproval, AIAction, ToolExecution, Verification, Permission and
AuditLog domain entities remain unchanged. ActionApproval is the mutable persistence
representation; evidence here is an in-process capability-like value, not a duplicate
database entity. No domain mapping, persistence boundary or writes are added.

Step 11O's lifecycle policy and ActionLifecycleService remain unchanged. Its direct
ApprovalRequired -> Approved transition still returns ApprovalRequired. The only new
route to Approved is the approval workflow's narrow internal ActionDescriptor.Approve
method, requiring server-issued evidence bound to the exact waiting action. The
ordinary internal Transition method now also enforces Step 11O policy, prohibiting
generic internal approval/execution bypass. ApprovalId is populated only by Approve
and preserved on later allowed cancellation. ToolExecutionId/VerificationId remain
null, IsVerified remains false and RequiresApproval remains true. Step 11O documentation
describes the prior stage; Step 11P adds this specific trusted approval path.

## Trusted input, preconditions and ownership

ApprovalRequest contains exactly one decision enum: Approve or Reject. It has no
IDs, timestamps, status, tool selector, arbitrary text/comments, arguments, dictionaries,
JSON object, commands, script, URLs, paths or secret fields. Action and scope arrive
as existing server-resolved application values, never public/chat request payloads.
No model output or action summary is consumed as approval input.

For either decision, DecideAsync:

1. Checks cancellation, non-null request/action, defined decision and three nonempty
   scope Guids (user/conversation/project).
2. Requires exact action.Scope equality and status ApprovalRequired.
3. Freshly calls IActionApprovalScopeValidator, checking cancellation even if it denies.
4. Resolves action.ToolId through the sealed server-instance registry, requires exact
   descriptor ID and Enabled=false, and discards the handler reference.
5. Revalidates ownership immediately before issuing the decision and rechecks cancellation.
6. Reads a server UTC clock value, rejecting default/non-UTC/backwards issue times.
7. Approves or rejects the immutable action value without execution or persistence.

The dedicated validator contract requires authenticated current user, owned conversation,
exact project association and owned project under OwnedConversations rules. Existing
project-file Permission conventions are not reused or invented as tool permissions.
There is no production scope-validator implementation: fake tests establish service
behavior, not live authorization. Production wiring must ensure the explicit decision
call originates in a trusted human-facing application path; approval request data alone,
project ownership, registration or AI text does not establish user intent.

Cross-user/project/conversation scope returns ScopeDenied without action/evidence.
Proposed, Approved, Rejected, Executing, Succeeded, Failed, Cancelled and unknown
states are ineligible. Unknown/no-longer-registered tools return UnknownTool.
Descriptor mismatch or an enabled tool returns DecisionDenied (currently impossible
with the immutable Step 11N registry/descriptors, but explicitly guarded).

## Exact approve/reject flows and evidence binding

Approve generates a new nonempty ApprovalIdentifier Guid on the server. Evidence
contains only ID, exact ActionIdentifier, exact ActionScope (user/conversation/project),
exact ToolIdentifier, decision Approve (the approved decision) and UTC IssuedAt.
It has no public constructor, setter/init property, copy-with facility or public
issuance/deserialization factory. Identifier creation and evidence issuance are
internal to the trusted Application assembly; private constructors prevent external
caller construction through supported APIs/JSON. This is not protection against
malicious reflection or compromised trusted application code.

ActionDescriptor.Approve requires ApprovalRequired, no existing approval link and
matching action/scope/tool evidence, and creates a new Approved action linked to the
generated approval ID. Creation time/summary/tool/scope/identity are preserved; update
time becomes evidence issue time. Original waiting value remains unchanged.

Reject creates a new Rejected action through the existing policy-approved transition.
It returns no Approved evidence or approval ID. No rejection evidence is fabricated.

MatchesBinding checks exact action ID, scope and tool. ApprovalValidation's
IsBoundToApprovedAction additionally requires Approved state and matching approval
link. Mismatched action/user/project/conversation/tool and non-Approved values fail
closed. These methods establish binding only, not ownership, permission, current
durable state, expiry or execution eligibility. No caller-chosen evidence is accepted
by workflow request or by direct ActionLifecycleService.

Approval is not a permission grant, execution or verification. Future execution must
still require fresh authenticated ownership, actual required permission, valid approval,
an authorized execution workflow and subsequent verification. Approved is the end
state of this step; no transition to Executing is enabled.

## Replay, expiry and resource limits

Evidence cannot match another action, scope or tool, and consumers cannot mutate its
bindings. There is no durable decision store, action version, atomic compare-and-swap,
consumption state or durable single-use/replay guarantee. An old retained immutable
ApprovalRequired value can receive another decision and another approval ID. Deciding
an actual Approved value is denied. Double scope validation detects tested revocation
during calls but is not an atomic transaction with future state/ownership changes.

Expiry is explicitly deferred: evidence has no expiry field, lifetime or claim that
it expires. Future consumers must not treat binding checks as a complete authorization
check or use evidence for production execution until expiry/revocation/consumption and
current-state enforcement are designed and verified. Evidence is in-process only;
no signing, cross-process token or trusted persistence reconstruction is implemented.

- Request: one two-value enum; zero arbitrary text or arguments.
- Scope: three nonempty Guids; action and approval IDs: server-generated Guids.
- Evidence: fixed-size IDs/decision/timestamp plus existing ToolIdentifier (maximum
  64 original characters); no collection or payload expansion.
- Fixed result messages: at most 80 characters, no caller/error interpolation.
- Existing bounded action summary (160 characters) is neither copied into evidence
  nor interpreted as approval; no new summary/payload limit is introduced.

Denied results contain neither action nor evidence. Public Denied cannot create
Success/evidence, and invalid result statuses map to fixed Failed. Non-cancellation
dependency exceptions become fixed Failed without secrets/paths/stack traces/DB/tool
errors. OperationCanceledException propagates unchanged. Async methods accept tokens,
with checks before dependencies, after both scope validations and before decision
creation. No retries or fallback.

## Non-goals and verification

No IToolHandler/IToolDispatcher execution, shell/PowerShell/cmd/Git/filesystem/browser/
HTTP/MCP/OS work, provider/tool/function calling, automatic approvals or chat-triggered
decisions are added. DisabledToolDispatcher and ToolDescriptor.Enabled are unchanged.
AiChatService/router/providers remain approval/action/tool-free; AiChatRequest is still
Prompt-only. Memory/RAG integration stays deferred. Safe File Access remains disabled
by default and is not reused by approval. There are no automatic ActionApproval,
AIAction, ToolExecution, Verification, AuditLog or Permission writes, package additions,
project-file/schema changes or migrations. No real PostgreSQL/OpenAI/Ollama calls.

ApprovalChecks adds 74 pure/fake checks for successful explicit approve/reject,
immutable evidence/JSON rejection, exact bindings, IDs/timestamps, initial/ineligible
states, direct lifecycle denial, no execution, mismatches/unknown tools, invalid input,
double revalidation/revocation, safe failures, cancellation, clock validation, bounded
messages, honest replay/expiry limitations and unchanged chat/tool/file boundaries.
Unreachable execution/completion states are tested through the pure eligibility
predicate; actual reachable non-waiting states are tested through the service. All
existing checks are preserved. Existing isolated filesystem fixtures remain unchanged;
the new approval checks perform no filesystem/network/OS access.

SDK version was verified as user-local .NET 10.0.401.
`dotnet build Aura.slnx --no-restore` passed with zero warnings/errors.
`dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore`
passed all 632 checks (558 existing plus 74 Step 11P checks), without restore.
`git diff --check` and separate untracked-file whitespace checks passed. Git status,
diff/stat, full new-file contents and exact untracked-file inventory were inspected.

A separate review-only pass covered dependency direction, supported-API evidence
construction, exact binding, narrow lifecycle integration, default denial, no permission
conflation/execution/persistence/chat integration, cancellation, fixed errors and honest
replay/expiry claims. No remaining Critical/High/Medium/Low implementation defects
were identified. Production ownership/human-intent wiring, permissions, persistence,
replay/expiry/revocation, verification, PostgreSQL/pgvector and live providers remain
deferred/unverified. Build/static/fake verification is not production authorization
or execution verification. Suitable for separate human review before commit, without
a production-readiness claim. No commit, push or PR was created.
