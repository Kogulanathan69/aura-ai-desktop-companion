# Step 11J: explicit local AI chat orchestration foundation

Adds scoped IAiChatService/AiChatService in Application, minimal Prompt request,
safe response/status DTOs and one authorized API operation. Application uses only
ILocalAiProvider and provider-neutral validation/results, never Ollama-specific types.
The existing conversation ownership predicate is extracted into a shared internal
ConversationOwnershipQueries helper; ConversationService delegates to it without
changing its storage-only message behavior. No new package, schema or migration.

## Operation and authorization

POST /api/conversations/{conversationId:guid}/ai/generate, RequireAuthorization.
Body: Prompt only. No identity, role, model, provider, assistant content, system prompt,
or context controls. No generic provider diagnostic endpoint. Ordinary POST /messages
remains storage-only and never calls AI. Archived conversations accept explicit AI chat
just as they accept normal messages: archive remains organizational, not authorization.

Resolve authenticated LOCAL identity; query one conversation scoped by its UserId and
consistent General/null or Project/owned-link scope. Foreign/missing/inconsistent scope
returns the same NotFound without provider calls or persistence. For linked scope the
owned-project EXISTS stays within that conversation query. Normalize the current prompt
with the existing 16,384-original-UTF-16-code-unit provider-neutral limit; trim and require
non-whitespace; additionally reject NUL, which PostgreSQL text cannot store. No history,
Summary, project metadata, tasks, memories, files or other private context is queried to
construct a prompt. Only new AiGenerationRequest(normalizedCurrentPrompt) reaches AI.

## Persistence phases and outcomes

1. Create server GUID User/Text message, null provider/model/token count, server CreatedAt.
   Set parent LastMessageAt/UpdatedAt to that same time. One SaveChangesAsync.
2. Invoke ILocalAiProvider exactly once, outside any explicit DB transaction.
3. Provider failure returns GenerationFailed, the saved UserMessage and safe provider
   category. Keep user history, no fake assistant, retry, fallback or automatic memory.
4. On Success validate generated text and metadata, then fresh AsNoTracking ownership/
   scope check including original Type/ProjectId. Persistent ownership/scope change returns
   Denied with no content DTOs. The initial tracked Conversation is reused for timestamps;
   no second entity is attached. This is not uninterrupted authorization.
5. Create server GUID Assistant/Text message from generated untrusted text, safe provider
   identifier <=30 chars, model identifier <=100 chars, null TokenCount, server time.
   Set parent timestamps again and use one second SaveChangesAsync. Return Success only
   after that save returns, with UserMessage and AssistantMessage DTOs.

Generated content must be non-whitespace, NUL-free, valid UTF-8 text and <=1,048,576 UTF-8
bytes; original character length is checked before byte counting. Model/provider metadata
must be nonempty and limited to ASCII letters/digits and . _ - : /. Never truncate model
identity or store a raw transport response. Invalid results retain user history and return
GenerationFailed/InvalidResponse. Output is stored as untrusted text only, never executed.

Response fields: Status, optional UserMessage, optional AssistantMessage, optional safe
GenerationStatus. HTTP 200 for saved success, 404 foreign/missing, 403 post-generation
denial, 503 provider failure (including disabled), 500 safe PersistenceFailed. Existing
AppValidationException handling returns safe 400. No raw exceptions, internal URLs, provider
body, stack traces or generated failure content are returned or deliberately logged.

DbUpdateException on first save returns PersistenceFailed without message DTOs and never
invokes AI. DbUpdateException on second save returns PersistenceFailed with only the
previously saved UserMessage; no generated answer or claim of persisted assistant. Scoped
context is disposed by the endpoint lifetime; after failure it must not be reused for
another SaveChanges because failed pending entities may remain tracked. Other unexpected
exceptions retain existing generic API error handling. No distributed atomicity is claimed.
Real database/network faults can leave commit outcome uncertain; clients must inspect
history rather than assume that every failed/cancelled acknowledgement means no commit.

Caller cancellation flows to identity, all queries/saves and provider. Before first
save: no provider invocation. After first successful save during generation: user remains;
cancellation propagates and no assistant is saved. After generation before second save:
user remains, generated text is discarded. Cancellation during a database commit has
the usual uncertain acknowledgement semantics; no cross-phase atomicity or auto retry.

## Query and transaction pattern

Excluding standard local-user lookup: initial tracked conversation query (including owned
project EXISTS) = one SELECT; first message insert plus parent update = one SaveChanges;
one provider call; successful result adds one untracked ownership/scope SELECT; assistant
insert plus parent update = second SaveChanges. Provider/validation failures after the
first save skip second query/save. No N+1 or history query. No explicit transaction is
held across AI; each save follows existing relational EF transaction semantics. Real
rollback, provider SQL translation, HTTP/JWT and concurrent persistence remain unverified.

## Defaults, scope and findings

Step 11I remains disabled, so default authorized requests save a user prompt and return
GenerationFailed/Disabled with zero HTTP. Appsettings are unchanged. Step 11F remains
disabled by options, unmapped content route, service gate and reader gate. No cloud
fallback, OpenAI/Groq/Tavily, embeddings/vector search, ProjectMemory retrieval/creation,
file context, tool/action records, filesystem/process/shell/Git execution or content logs.

- Critical: none identified in this new operation.
- High: none identified in this new operation. Existing Step 11F filesystem TOCTOU
  remains separate if enabled; default is disabled. Local daemon privacy is a deployment
  prerequisite inherited from Step 11I, not proven by loopback routing/model-name checks.
- Medium: timestamps may regress under concurrent message saves. User/assistant pairs
  can interleave; the schema has no turn/pair linkage or concurrency token. Two saves
  intentionally permit partial success; no atomic DB+AI workflow is offered.
- Medium: no idempotency key or retry suppression; client retries, particularly on 503
  after the user save, can duplicate user prompts and generation. No automatic retry here.
- Medium: per-operation bounds do not provide aggregate request, storage, CPU/GPU quotas
  or rate limits. Existing unbounded conversation/message history remains deferred.
- Medium: post-generation validation is point-in-time; ownership/scope changes after checks
  or change/revert cycles can be missed. Normal metadata APIs cannot retarget scope, but
  privileged database changes and deletion races still require runtime testing.
- Low: Step 11I allows model selectors up to 128 chars while Message.ModelName allows
  100; such results fail safely after the user save instead of truncating identity.
- Low: generated text is untrusted; future UI must render it safely, not as executable HTML
  or tool authorization. No rendering or tool pathway is added in this change.

## Verification

Required solution build passed with zero warnings/errors; full package-free suite passed
213 checks (33 new chat checks plus 180 existing). Chat checks use a provider double and
staged message writes that commit only when the save hook succeeds. They verify authorization,
exact prompt, reject history queries, no memory/file query dependencies, model/role overposting,
archive behavior, phase ordering, provider statuses, invalid output/metadata, both save failures,
caller cancellation and post-provider ownership changes. One integration-of-components check
uses the real default-disabled Ollama provider with a fake HTTP handler to prove zero HTTP.
The ordinary message storage path and all 11F/11G/11H/11I checks still run.

No real Ollama, database or AI model was contacted. Tests do not establish actual SQL,
transactions, rollback or concurrent runtime behavior. Before deployment exercise real
JWT/HTTP, PostgreSQL phase/commit failures, cancellation timing, duplicate requests, concurrent
turns/timestamps, output rendering and a trusted cloud-disabled local Ollama daemon.
NU1900 still reports unavailable vulnerability metadata; scanning is not verified.
No commits/pushes, migrations, schema/package changes or feature enablement.
