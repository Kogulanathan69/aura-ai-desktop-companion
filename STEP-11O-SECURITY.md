# Step 11O — AI action / action lifecycle foundation

## Scope and architecture

Application owns ActionIdentifier, ActionScope, ActionProposalRequest,
ActionDescriptor, lifecycle and operation statuses, ActionOperationResult,
ActionTransitionPolicy, IActionScopeValidator and IActionLifecycleService/
ActionLifecycleService. No API endpoints, DI registrations, infrastructure adapters,
chat integration or persistence are added. Only application files, fake checks,
the suite invocation and this document change.

The existing AIAction, ActionApproval, ToolExecution, Verification, Permission and
AuditLog domain entities were inspected and remain unchanged. AIAction is the
existing mutable persistence representation with a string Status; the new immutable
descriptor is an application lifecycle value, not a duplicate persistence entity.
No mapping to domain rows or string statuses is implemented. There is no store
interface because no lifecycle persistence operation is needed in this step.

## Proposal and server scope

ActionProposalRequest contains only an existing validated ToolIdentifier and bounded
Summary. It cannot select ID, scope, timestamps, initial status, approval evidence,
execution links or verification. ActionIdentifier is a nonempty server-generated Guid.
Every new descriptor begins Proposed, RequiresApproval=true and IsVerified=false.
There is no public descriptor constructor or mutable property. Transition returns a
new value, preserving ID, scope, tool, summary and creation time. Clock supplies
creation/update timestamps; no caller timestamps are accepted.

ActionScope requires nonempty user, conversation and project Guids. General/no-project
actions are unsupported in this foundation because current tool descriptors require
owned scope. Scope is an internal application boundary, never a public request DTO.
IActionScopeValidator must authenticate the current user and verify owned conversation,
exact conversation/project association and owned project following OwnedConversations.
There is no production implementation: authorization is a deferred boundary, not a
claimed production guarantee. Each operation validates scope; transitions first compare
the entire descriptor scope, returning no descriptor on mismatches. No cross-project,
cross-user or cross-conversation transition is allowed even with a valid action ID.

## Exact lifecycle graph and effective policy

States: Proposed, ApprovalRequired, Approved, Rejected, Executing, Succeeded,
Failed, Cancelled.

| Current | Target | Step 11O operation decision |
| --- | --- | --- |
| Proposed | ApprovalRequired | Success |
| Proposed | Cancelled | Success |
| ApprovalRequired | Approved | ApprovalRequired; no trusted approval workflow exists |
| ApprovalRequired | Rejected | Success |
| ApprovalRequired | Cancelled | Success |
| Approved | Executing | ExecutionDisabled |
| Approved | Cancelled | Success in policy; Approved cannot be created by this service |
| Executing | Succeeded | ExecutionDisabled |
| Executing | Failed | ExecutionDisabled |

IsDefinedTransition describes these nine graph edges only; true does not authorize
anything. Evaluate enforces the Step 11O restrictions. The service applies Evaluate
before constructing a transitioned descriptor. All other pairs, unknown enum values,
self-transitions, terminal exits and skipped states return TransitionDenied. Rejected,
Succeeded, Failed and Cancelled are terminal. No automatic retry/fallback and no
Executing cancellation edge are supported. Proposed cannot go directly to Approved,
Executing or Succeeded; ApprovalRequired cannot skip to Executing.

Proposal is not approval. Approval is not execution. Execution or success is not
verification. The current service can produce only Proposed, ApprovalRequired,
Rejected and Cancelled. Conceptual approval/execution/completion edges are represented
but intentionally not performed without future trusted workflows. It never fabricates
Approved status or evidence. ApprovalId, ToolExecutionId and VerificationId are nullable
Guid linkage slots permanently null in this step; they are not evidence contracts.
Actual approval evidence issuance/validation remains Step 11P work. Verification
workflow and linkage population remain deferred.

## Tool, permission and execution boundaries

Proposals resolve metadata for exactly one existing server-known ToolIdentifier.
Unknown IDs return UnknownTool. Registry lookup discards the handler reference;
neither IToolHandler nor IToolDispatcher is invoked. Disabled tools may be described
in a proposal, but registration/proposal/permission metadata is never authorization.
ToolDescriptor.Enabled stays false and DisabledToolDispatcher is unchanged. No tool
permission grants are invented. Future work must validate resource-specific tool
permissions and trusted approval before any real action or execution transition.

No shell/PowerShell/Git/filesystem/browser/HTTP/OS/API execution is added. No provider
tool/function calling, automatic proposal from model output, RAG integration or
AiChatService/router/provider changes occur. Ordinary /messages stays storage-only.
Safe File Access remains disabled by default and is not reused by this foundation.

## Bounds, untrusted display text, safe results and cancellation

- Action ID: one server-generated Guid (128 bits); no arbitrary ID string.
- Tool ID: existing ToolIdentifier limit, 64 characters before normalization.
- Scope: three nonempty Guids, no unbounded collections or arbitrary scope payload.
- Summary: original input at most 160 ASCII characters before trim/allocation.
  Allowed characters: ASCII letters/digits, space, hyphen, parentheses, comma,
  exclamation mark and question mark. Blank, controls/NUL, surrogate/non-ASCII,
  oversize, path/URL/script/JSON syntax are rejected. Tool-specific arguments,
  dictionaries, JSON payloads and command/secret fields are absent.
- Result messages: fixed application text, at most 80 characters; no arbitrary
  raw exception/provider/tool/DB/OS output or internal paths/type names.

Summary is untrusted display data only, never instructions/authorization/provider
context. Existing PrivacyGuard adds a veto: Redact or Block is rejected, never
silently sanitized into an approved action. Neither the narrow alphabet nor pattern
rules guarantee detection of every unlabeled secret/private fact; callers must not
place confidential text in summaries. There is no persistence, provider transmission
or production display surface here. This is not a confidentiality classifier.

Denied results carry no action descriptor. Public Denied factory cannot fabricate
Success; unsupported operation status becomes fixed Failed. Non-cancellation dependency
exceptions map to a fixed Failed result without echoing input/error details. Cancellation
propagates unchanged, checked before dependencies and after async scope validation.
All async service/boundary methods accept CancellationToken. No retry or fallback.

Pure values have no current-state store or concurrency control. A caller retaining
an old value could branch from it; these operations do not implement durable ordering,
linearizability or approval capabilities. Future persistence must enforce versioned
state and ownership atomically. No automatic AIAction, ToolExecution, ActionApproval,
Verification, AuditLog or Permission writes are introduced.

## Verification and deferred runtime

ActionChecks uses only pure logic and synthetic scope/clock/never-called-handler
fixtures. It exhaustively compares all 64 state pairs against the exact graph and
tests effective default denial, initial state, immutability, identity/scope preservation,
summary limits/unsupported data, foreign scope denial, revalidation, fixed failure
messages, cancellation and unchanged chat/file-access/tool boundaries. All previous
security checks remain included. The suite's pre-existing isolated filesystem fixtures
remain unchanged; the new action checks do not access filesystem/network/OS resources.

User-local .NET 10 at
`C:\Users\Ut011331\AppData\Local\Microsoft\dotnet\dotnet.exe` was used.
`dotnet build Aura.slnx --no-restore` passed with zero warnings and errors.
`dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore`
passed all 558 checks (426 existing checks plus 132 Step 11O checks).
Review found and fixed cancellation propagation when a validator cancels the token
but returns denial without throwing; the final build and suite include that fix.
`git diff --check` and separate untracked-file whitespace checks passed. Git status,
diff/stat, full new-file contents and exact untracked-file inventory were inspected.

A separate review-only pass covered dependency direction, exact lifecycle edges,
default denial, no execution/approval fabrication/persistence/chat integration,
scope isolation, limits, safe messages and deferred runtime assumptions. No remaining
Critical/High/Medium/Low implementation defects were identified. The foundation is
suitable for separate human review before commit, not production execution readiness.
No commit, push or PR was created. No packages, project files, migrations or schema changes.
No real PostgreSQL/OpenAI/Ollama calls. PostgreSQL/pgvector runtime, production
scope validation, permission enforcement, approval/verification workflow, persistence,
real execution, semantic retrieval and live providers remain deferred/unverified.
Build/fake checks are not production authorization or execution verification.
