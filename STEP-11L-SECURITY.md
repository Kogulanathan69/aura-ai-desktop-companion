# Step 11L: explicit AI provider routing foundation

## Scope and architecture

Before: AiChatService -> ILocalAiProvider. After: AiChatService -> IAiProviderRouter
-> exactly one of ILocalAiProvider / ICloudAiProvider. Application owns the neutral
router, interface and options; Infrastructure retains all transport details. API
only binds server configuration and registers DI, with no endpoint routing policy.
Existing local/cloud registrations and disabled defaults are preserved. No package,
schema, migration, endpoint, memory/RAG, tools/actions or execution feature is added.

## Configuration and policy

AI:Router:Mode accepts the enum Local or Cloud. Missing configuration defaults Local,
preserving Step 11J behavior. Router options contain only Mode; no key/model/URL.
The options object is a startup-bound immutable snapshot, not a caller/runtime
toggle. Named values that cannot bind to the enum fail startup before any generation;
out-of-range numeric enum values return InvalidConfiguration and call neither
provider. No unknown mode defaults to Cloud or silently switches providers.
Cloud requires deliberate server selection AND valid enabled OpenAI configuration;
selecting a disabled provider passes Disabled through without fallback. Both
providers remain disabled by default, and startup needs no cloud secret or daemon
for disabled provider configuration. Existing API database/JWT configuration
requirements are unchanged; this step adds no database startup access.

Exactly one selected provider invocation per valid, uncancelled routing request.
No automatic retry, fallback in either direction, race, scoring or load balancing.
All selected results (including failure statuses) and provider/model metadata pass
through unchanged. Exceptions propagate; they never trigger another provider.
Malformed success output remains subject to Step 11J validation before persistence.
The router does not return configuration secrets or produce logging.

## Input, authorization and persistence

Caller DTOs still contain only Prompt; no provider/model/context selector. Router
receives the existing normalized explicit prompt unchanged. No history, Summary,
project metadata, memory, files or tools are retrieved or added. Cloud selection
means that this explicit prompt is sent to OpenAI when enabled; no cloud consent UI
is added. Administrator configuration must reflect the deployment's privacy policy.

Only AiChatService's generation dependency changes. Initial identity/ownership,
prompt validation, user save, one generation invocation, valid-success fresh scope
check and assistant save are unchanged. Provider failures preserve user history;
post-generation denial discards output; failed assistant save never claims success.
Archived conversations still permit explicit generation. Ordinary POST /messages
remains storage-only. Existing partial-save, acknowledgement uncertainty, client
retry duplicates, timestamp regression and concurrent turn interleaving remain.

Cancellation is checked before routing and the same token is forwarded unchanged.
Selected-provider cancellation propagates without retry/fallback. Chat cancellation
checks and database token propagation are unchanged. A pre-cancelled router request
throws even if the selected provider is disabled; default chat already checks the
token before generation. Provider deadlines/error sanitization remain in providers.

## Verification and separate review

dotnet build Aura.slnx --no-restore passed with zero warnings/errors. Complete
package-free suite passed 326 checks: 26 new routing checks and all 300 previous.
Previous chat checks now exercise the actual Local router, preserving their
assertions. Routing checks cover both selections/all statuses, exact unchanged
input/result/token, one call, no alternate call/retry/fallback, invalid numeric modes,
binding/defaults, precancellation/selected cancellation, minimal options/DTOs,
chat dependency direction and a two-phase Cloud orchestration success with fakes.
All HTTP checks use fake handlers; no real OpenAI/Ollama/PostgreSQL call occurred.
No real API key was supplied. PostgreSQL migrations/runtime and live provider
verification remain intentionally deferred. NU1900 reports unavailable package
vulnerability metadata; vulnerability scanning is not verified.

Separate review-only pass inspected all tracked/untracked changes, DI direction,
single-call control flow, safe defaults, unchanged orchestration body, no input
expansion, no secret/model/URL router options and no unrelated edits. git diff
--check passed with line-ending conversion notices only. No commit/push/PR.

## Findings and runtime limitations

Critical/High: none identified in this foundation. Medium: explicit server Cloud
selection exposes submitted prompts under OpenAI's retention policy; store:false
does not promise zero retention. No per-user cloud consent, rate/concurrency/cost
quota or idempotency mechanism is introduced. Existing Step 11J concurrency and
point-in-time authorization limitations remain. Low: invalid textual enum binding
stops startup rather than producing a generation result; provider access/model
compatibility is unverified. No automatic fallback is intentionally available.

Compilation, fakes and static inspection do not prove production secret loading,
account/model access, DNS/TLS, deployment privacy/retention settings, billing or
external instrumentation. Real PostgreSQL translation/transactions/commit faults,
concurrent persistence and JWT/HTTP behavior remain unverified. Historical Step
11J/11K documents describe their original steps; this document records the deliberate
replacement of local-only chat wiring by an explicit server-controlled router.
