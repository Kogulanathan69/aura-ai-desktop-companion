# Step 11N — Tool architecture foundation

## Scope and architecture

Application owns ToolIdentifier, ToolDescriptor, capability/permission metadata,
ToolInvocationRequest, fixed ToolExecutionResult/statuses, IToolHandler,
IToolRegistry/ToolRegistry and IToolDispatcher/DisabledToolDispatcher. Infrastructure,
Domain, API registration, public endpoints and provider transports are unchanged.
There are no production handlers or registered executable tools. The registry is
constructed only from explicit server-provided descriptor/handler instances; no
assembly scans, reflection-based execution, plugin loading or arbitrary type creation.

Existing ToolExecution, AIAction, ActionApproval, Verification, Permission and AuditLog
entities were inspected. They describe persistence, not an application execution
workflow. Existing permissions cover project-file access and enforce user/project/
resource scope and non-revoked grants. They are not repurposed as tool authorization.
OwnedConversations requires conversation ownership and ownership of the associated
project for Project conversations. No new ownership query or permission grant is added.

## Registry, identifiers and descriptors

Identifiers are immutable, bounded before normalization, trimmed and invariant-lowercase.
They start with an ASCII letter and contain only ASCII letters, digits and hyphens.
Paths, URLs, qualified type names and command separators are invalid. Registry IDs
are unique after normalization. Descriptor enumeration is a read-only snapshot sorted
by ID with ordinal comparison, independent of registration order. Lookup returns
exactly one explicitly registered descriptor/handler pair or no match.

Descriptors have immutable ID, display name, description, capability and required
permission classification. Display text accepts printable ASCII only. Undefined enum
metadata is rejected. These classifications are not grants or mappings to existing
Permission rows; an actual resource-scoped mapping must be designed later.
Enabled is permanently false, RequiresApproval and RequiresOwnedScope permanently
true. Consumers cannot enable a descriptor or mutate the registry after construction.

## Default deny, permissions and future approval boundary

DisabledToolDispatcher returns InvalidRequest for missing requests/identifiers,
UnknownTool for unknown IDs and Disabled for known IDs. It never calls IToolHandler.
Registration, AI text or request data cannot authorize execution. There is no enabled
branch, retry, fallback, approval-evidence flag or bypass based on metadata. Missing
permission/approval always leaves tools disabled; no permission or approval workflow
is simulated. The dispatcher deliberately depends on the concrete sealed registry
to keep its denial boundary free of externally implemented resolver behavior.

IToolHandler is a FUTURE execution contract only. It accepts CancellationToken and
returns fixed safe results. There are no implementations in Application/Infrastructure.
The only fake handlers are security fixtures, and the dispatcher never invokes them.
Future work must enforce: AI proposes -> authenticated owned scope and permission
check -> explicit user approval -> execute -> verify. Handler resolution is metadata/
configuration access, not an authorization capability. Adding a real handler or calling
it directly without that future workflow is outside this foundation's safety guarantees.

## Resource limits and safe results

- Registry: at most 32 entries; stop and reject at the 33rd yielded entry.
- Tool ID: at most 64 characters of original input, bounded before trimming/copying.
- Display name: at most 80 printable ASCII characters, nonblank.
- Description: at most 256 printable ASCII characters, nonblank.
- Invocation: one validated ToolIdentifier; zero arguments, zero argument keys/values.
  Tool-specific argument schemas and server-owned action/scope models are deferred.
- Result message: at most 80 characters; every current message is fixed application text.

Results expose only status and fixed message. No arbitrary result text, exception,
token, OS error, stack trace, path or file contents can be supplied through the result
factory. Invalid enum status becomes Failed. Success/PermissionDenied/ApprovalRequired
are reserved categories, not claims that execution/authorization occurred. The dispatcher
only returns InvalidRequest, UnknownTool or Disabled. Raw handler exceptions are
unreachable through it; actual execution exception translation remains future work,
with the handler contract requiring fixed safe failure results and cancellation propagation.
Registry configuration errors use fixed messages and never echo invalid input.

Cancellation is checked before dispatcher lookup and propagates as
OperationCanceledException. Future handler signatures explicitly accept a token.
There is no work to cancel downstream because the denial boundary never invokes handlers.

## Non-goals and limitations

No shell, terminal, PowerShell, Git, filesystem, IDE, browser, HTTP, email/calendar,
MCP or OS execution/access is added. No Process.Start, dynamic execution, real network
adapter, provider function/tool calling or AI tool invocation is introduced.
AiChatService and IAiProviderRouter remain tool-free; ordinary /messages is storage-only.
Step 11M RAG integration into chat remains deferred. Safe File Access remains disabled
by default, and no Step 11F services/configuration are enabled or reused by tools.

No ToolExecution/AIAction/Permission/Approval/Verification/Audit records are persisted,
and there is no DB dependency in the new architecture. No packages, schema, project
files or migrations are changed. No real PostgreSQL, OpenAI or Ollama calls are made.
PostgreSQL/pgvector and live provider runtime remain deferred. Permission enforcement,
approval provenance, production ownership checks, real handlers, execution failure
translation, tool-specific arguments, audit persistence and verification workflows
are unimplemented and unverified. This is not production action-execution readiness.

## Verification

ToolChecks adds package-free fake-only checks for normalized/invalid IDs, duplicate
and missing registrations, immutable snapshots, ordinal order, exactly one lookup,
registry bounds, descriptor bounds, missing invocation data, unknown/disabled tools,
no calls/retries/fallback even for a throwing handler, fixed result payloads, cancellation,
future token contract, absent argument/permission/approval flags and unchanged chat/
router/default file-access boundaries. All previous security checks remain included.
The existing suite's isolated filesystem fixtures remain unchanged; the new ToolChecks
perform no filesystem/OS/network execution.

User-local .NET 10 was used at
`C:\Users\Ut011331\AppData\Local\Microsoft\dotnet\dotnet.exe`.
`dotnet build Aura.slnx --no-restore` passed with zero warnings and errors.
`dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore`
passed all 426 checks (364 prior checks plus 62 Step 11N checks). No restore was
needed. `git diff --check` passed; untracked-file whitespace was checked separately.
Git status, diff/stat and exact untracked-file inventory were inspected.

A separate review-only pass covered Clean Architecture direction, default denial,
absence of execution/dynamic loading, permission and approval boundaries, unchanged
AI integration, fixed resource bounds, safe errors, cancellation and no persistence
or runtime DB assumptions. No Critical/High/Medium/Low implementation defects were
identified. Only the new application tool files, new fake checks, suite invocation
line and this document changed. Suitable for separate human review before commit;
these build/fake checks do not verify production permissions, approval or execution
behavior and do not establish production readiness. No commit, push or PR was made.
