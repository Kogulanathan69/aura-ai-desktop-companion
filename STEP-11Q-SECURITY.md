# Step 11Q — Verification workflow foundation

## Scope and architecture

Application's Verifications namespace owns VerificationRequest, ExecutionEvidenceInput,
execution report and verification outcome enums, VerificationIdentifier/Evidence,
VerificationOperationResult/statuses, VerificationContextPolicy,
IVerificationScopeValidator, IExecutionEvidenceSource, IIndependentVerificationPolicy
and IVerificationWorkflowService/VerificationWorkflowService. The plural namespace
avoids colliding with the existing Domain Verification entity.

The existing Verification, ToolExecution, AIAction, Permission and AuditLog entities
were inspected and remain unchanged. They are persistence representations; this step
introduces immutable application assessment evidence, not new database entities or
domain mappings. There is no persistence interface because no writes/read workflow
is needed yet. No API endpoints, DI registrations or production execution/proof/
ownership adapters are added. Clean Architecture direction remains Application to
Domain, with no Application dependency on Infrastructure.

## Relationship to action and approval workflows

ActionDescriptor integration is intentionally deferred until a trusted execution
workflow exists. No action is fabricated in Executing/Succeeded state, no ToolExecution
entity is constructed, and no action's status, ToolExecutionId, VerificationId or
IsVerified changes. Step 11O and Step 11P code and checks remain unchanged.

The foundation accepts Approved only as the immutable authorization context for a
separate future execution attestation, not as a claim that the action executed.
ApprovalValidation must confirm exact linked approval evidence. An actual execution
can be attested only by the injected trusted source, independently of the descriptor.
The supplied source returns null for every request because no execution workflow
exists. This allows the boundary to be checked with synthetic adapter DTOs without
inventing an execution lifecycle or marking actions verified. Production linkage and
state eligibility after Step 11R require separate design and verification.

Approval is not execution, verification or permission. ExecutionReportOutcome.Succeeded
is not proof of verification. Operation Success means an assessment produced evidence;
the evidence's Outcome separately states Verified, Failed or Inconclusive.

## Exact request, execution input and trust boundaries

VerificationRequest contains one server-known nonempty Guid ExecutionId. It cannot
select scope, outcome, proof, verification ID/timestamp, approval, logs, facts, text,
commands, paths, URLs, file contents, JSON or dictionaries. Existing ActionDescriptor,
ActionScope and ApprovalEvidence arrive separately as internal server-known values,
not public/chat request bodies. Public AiChatRequest remains Prompt-only.

IExecutionEvidenceSource.GetAsync receives exact scope and execution reference.
Its future trusted implementation must scope before retrieval and attest provenance
of an actual completed authorized execution. No such adapter is implemented here.
ExecutionEvidenceInput is a bounded data transfer record, NOT a capability or proof
by itself. It contains ExecutionId, ActionIdentifier, ActionScope, ToolIdentifier,
ApprovalId, ExecutionReportOutcome (Succeeded/Failed/Unknown), and UTC CompletedAt.
It can be constructed by future adapters and synthetic checks, but workflow callers
cannot submit it as a request; the workflow obtains it only from its trusted dependency
and independently checks every binding. UnavailableExecutionEvidenceSource returns
null without database, OS, filesystem, network or tool access.

IIndependentVerificationPolicy is the future independent proof boundary. A real policy
must establish tool-specific proof before returning Verified, without executing a
tool to manufacture proof. It is not selected by request data. The supplied
ConservativeVerificationPolicy never returns Verified: a failed report produces Failed;
Succeeded/Unknown produce Inconclusive. A failing/unknown execution report cannot be
promoted to Verified even by a faulty injected policy. Tool-specific proof schemas
and production proof adapters are deferred. The synthetic test policy exercises
the outcome boundary only and claims no real independent verification.

## Exact verification flow and preconditions

VerifyAsync:

1. Checks cancellation, non-null action/request and three nonempty internal scope
   Guids plus nonempty execution reference.
2. Requires exact action/scope equality and eligible Approved authorization context.
3. Requires actual immutable ApprovalEvidence matching action ID, scope, tool and
   action.ApprovalId; an approval ID alone is insufficient.
4. Freshly validates ownership, observing cancellation even on a nonthrowing denial.
5. Resolves exact server-registered tool metadata and requires the tool remains disabled.
   Discards the handler reference and never calls a dispatcher.
6. Resolves the execution input through the trusted source. Null is ExecutionUnavailable.
7. Validates exact requested execution ID, action ID, scope, tool and approval ID;
   defined report enum; UTC non-default completion time no earlier than approval.
8. Calls the independent policy, requiring a defined outcome and rejecting Verified
   for Failed/Unknown execution reports.
9. Revalidates ownership after the policy; checks a non-default UTC server issue time
   no earlier than execution completion/action update, and checks cancellation.
10. Issues immutable assessment evidence with a new server-generated Guid and timestamp.

IVerificationScopeValidator is a deferred application contract for authenticated
current user, owned conversation, exact conversation/project association and owned
project, consistent with OwnedConversations. No production DB adapter or permission
grant is invented. Exact scope mismatches and both ownership denials return no evidence.
Double validation detects tested revocation during calls but is not an atomic ownership/
state transaction. Missing/foreign approval, unknown tools, mismatched execution data,
invalid states/enums and unavailable sources fail closed.

## Evidence, bindings, resource bounds and safe errors

VerificationEvidence contains only VerificationIdentifier, ActionIdentifier, exact
ActionScope (user/conversation/project), ToolIdentifier, ApprovalId, ExecutionId,
VerificationOutcome and server UTC IssuedAt. Identifier/evidence constructors are
private, issuance is internal to the trusted Application assembly, and all public
properties are get-only. No supported public constructor, deserialization factory,
setter or copy-with capability allows caller/AI/approval/tool metadata to fabricate
evidence. This is not protection against malicious reflection or compromised trusted
adapters/application code.

MatchesBinding checks exact action, all scope values, tool, approval and execution
reference. Evidence cannot match a different one of those values; outcome is immutable
and set only through the workflow's policy/issuance path. Binding is not production
proof provenance, permission, expiry, current-state or consumption validation.

- Request: one nonempty 128-bit execution Guid, no text or collections.
- Action/approval/execution/verification and three scope IDs: fixed Guids.
- Tool ID: existing maximum 64 characters before normalization.
- Input: one fixed enum and DateTime in addition to bounded IDs; zero arbitrary facts.
- Evidence: one defined three-value outcome enum and server DateTime plus bounded IDs.
- Result messages: fixed application text, at most 80 characters; zero raw errors/logs.

Public Denied factory cannot create Success/evidence; unknown operation status maps
to fixed Failed. Non-cancellation scope/source/policy/clock exceptions map to a fixed
Failed result without raw exceptions, paths, secrets, provider/DB/OS details or stack
traces. Cancellation propagates unchanged, checked before dependencies, after each
async boundary and before issuance. Every async source/policy/validator/service accepts
CancellationToken. There is no retry, fallback or unbounded enumeration.

## Replay, expiry, non-goals and deferred work

Bindings and immutability are implemented. Durable replay prevention, single-use,
consumption, execution versions and current-state concurrency are NOT implemented.
A stale execution reference may be assessed again and receive a new verification ID.
There is no persistence or atomic state transition. Expiry is explicitly deferred;
there are no expiry/consumption fields or lifetime guarantees. Evidence is in-process
only, with no cross-process signing or trusted reconstruction from stored rows.

No IToolHandler/IToolDispatcher execution, shell/PowerShell/cmd/Git/filesystem/browser/
HTTP/MCP/OS mutation or tool access is added. Tools remain disabled. No AI/provider
function/tool calling, chat/router integration, automatic verification from AI text,
approval UI, real ToolExecution workflow or fake domain ToolExecution is introduced.
Safe File Access remains disabled by default and is not reused by verification.
No automatic Verification, ToolExecution, AIAction, ActionApproval, AuditLog or
Permission persistence is added. No packages, project-file/schema changes or migrations.
No real PostgreSQL/OpenAI/Ollama calls were made.

Remaining unverified work includes real execution provenance, tool-specific independent
proof, action linkage/state integration, production ownership/permission adapters,
approval revocation/expiry/consumption, persistence/concurrency, PostgreSQL/pgvector
and live providers. The supplied source cannot attest any actual execution; synthetic
Verified evidence is a test of the boundary, not production verification readiness.

## Verification and review

VerificationChecks adds 97 package-free fake/pure checks for immutable issuance and
JSON rejection, exact action/scope/tool/approval/execution/outcome binding, request/input
bounds, approval linkage, context eligibility, mismatches/nulls/enums/timestamps,
source unavailability, no false success, conservative policy, invalid proof outcomes,
revocation/revalidation, safe dependency failures, cancellation, replay/expiry honesty,
no action mutation/execution, and unchanged chat/file-access boundaries. All 632 prior
checks remain included. New checks use synthetic DTOs and a never-called handler;
they never create a ToolExecution/domain execution or fabricate an Executing action.
The suite's pre-existing isolated filesystem fixtures remain unchanged.

User-local SDK was verified as .NET 10.0.401. After correcting the new namespace
collision with the existing Verification entity, `dotnet build Aura.slnx --no-restore`
passed with zero warnings/errors. The full package-free suite
`dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore`
passed all 729 checks (632 prior plus 97 new); no restore was needed.
`git diff --check` and separate untracked-file whitespace checks passed. Git status,
diff/stat, full new-file contents and exact untracked-file inventory were inspected.

A separate review-only pass covered dependency direction, supported-API evidence
issuance, exact bindings, no false Verified action state/fake execution, approval
separation, default source/policy, no tool/chat/persistence integration, cancellation,
safe errors and honest replay/expiry/runtime claims. No remaining Critical/High/Medium/
Low implementation defects were identified. Suitable for separate human review before
commit, without a production-readiness claim. Build/static/fake checks do not verify
production execution provenance or independent tool effects. No commit, push or PR.
