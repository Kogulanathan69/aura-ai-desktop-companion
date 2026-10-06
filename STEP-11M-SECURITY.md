# Step 11M — Memory / RAG integration foundation

## Scope and architecture

Application-layer contracts, bounded retrieval policy, context assembly and fake-only
security checks are implemented. AiChatService integration is intentionally deferred.
AiChatService -> IAiProviderRouter -> exactly one provider is unchanged. Ordinary
/messages remains storage-only. There are no new endpoints or DI registrations.

ProjectMemoryService already supports manual CRUD but its list reads are unbounded;
retrieval deliberately does not call that list or load entities/embeddings. Existing
ProjectContextService (the repository's context builder equivalent) includes file
metadata and free-form project/session text; none is imported automatically.

IProjectMemoryRetriever accepts server-known user/conversation/project scope and the
normalized current prompt. IProjectMemoryCandidateSource is the future semantic
retrieval adapter boundary. No implementation queries PostgreSQL or pgvector.
Bounded DTOs contain only memory ID, project ID and content.

## Ownership and privacy

IProjectMemoryScopeValidator is a deferred adapter contract, not an implemented
authorization guarantee. It must freshly validate authenticated identity, owned
Project conversation, matching project association and owned project, following
OwnedConversations rules. Null/general scope and empty IDs return no memory before
source access. Validation occurs before and after retrieval. Each returned item's
ProjectId is checked independently; duplicate/conflicting IDs are omitted.
Production identity/scope resolution and runtime race behavior remain unverified.
Public AiChatRequest still contains only Prompt; scope records are application
contracts and must never be bound to public request bodies.

IProjectMemoryTextPolicy requires independently established safe approval/provenance.
The supplied DenyProjectMemoryTextPolicy denies ALL text. Existing manual memory
records have no safety/provenance approval field; Manual or IsSensitive=false is
not treated as approval. No schema or approval workflow is added. PrivacyGuard
is an additional veto: both Redact and Block cause omission. Allow alone is not
proof of safety; its pattern rules cannot identify every secret, private fact or
sensitive file excerpt. The permissive fixture policy exists only in checks.
Do not wire a permissive policy into production. Integration remains deferred until
real authorization and safe text provenance can be demonstrated.

## Deterministic limits and format

- At most 32 candidate MoveNext calls, with no request for candidate 33.
- At most 4 retrieved memories, ordered by Guid ascending within the bounded window.
- At most 512 UTF-16 characters and 2048 strict UTF-8 bytes per memory.
- At most 2048 raw memory characters and 2048 characters of formatted memory context.
- At most 16384 characters in the final generation prompt, including framing and
  the full normalized user prompt. Memory is omitted to preserve a maximum prompt.

Blank/null items, empty IDs, controls (including line breaks/NUL), invalid surrogate
sequences, oversize text, mismatched projects and duplicate IDs are skipped. Text
is rejected before trimming/copying/privacy evaluation; it is never truncated to
hide evidence. Candidate storage is fixed at 32 bounded records. A future source
must bound database projections and allocations too; the consumer cannot prevent
allocations already performed by a faulty adapter. Ranking, query size and stable
candidate selection are deferred; ordering only covers the bounded source window.

The assembler uses single-line quoted bullets with escaped quotes/backslashes,
an explicit untrusted-reference rule and a separate current-user-request section.
No XML/JSON execution delimiters or provider fields are introduced. Retrieved text
cannot authorize tools/actions/scope changes. These labels are not a model-level
prompt-injection guarantee. There are no tools or provider calls in this foundation.
BoundedAiContext is an application result, not an authorization capability; do not
accept externally constructed instances as approved provider input.

## Non-goals and verification

No automatic ProjectMemory writes, summaries, access-counter writes, personal
memory retrieval, project file retrieval, filesystem/browser/microphone/screenshot
access, embeddings, vector search, tools/actions or agent workflows were added.
Safe File Access remains disabled by default; its configuration is unchanged.
No packages, project files, schema or migrations were changed. No real
OpenAI/Ollama/PostgreSQL calls were made. Existing suite fake HTTP handlers and
database doubles remain unchanged. Its pre-existing isolated filesystem tests
are retained and do not enable application file access.

RetrievalChecks adds fake-only coverage for scope denial, cross-project filtering,
bounded lazy enumeration, limits, malformed text, privacy vetoes, default denial,
ordering, duplicate IDs, framing/escaping, full prompt preservation, scope revocation,
cancellation and unchanged public/provider boundaries. Earlier checks remain intact.

.NET SDK 10.0.401 was installed user-locally. `dotnet restore Aura.slnx`
completed successfully, and `dotnet build Aura.slnx --no-restore` passed.
The security-check project was restored separately because it is not covered by
the solution restore. The complete package-free security suite
`dotnet run --project security-checks/Aura.SecurityChecks.csproj --no-restore`
passed with 364 checks. `git diff --check` passed.
PostgreSQL/pgvector runtime, production ownership validation, semantic retrieval
and ranking, and live OpenAI/Ollama runtime remain explicitly deferred and unverified.
AiChatService integration remains deferred.

A separate review-only pass covered architecture, scope, privacy, bounded allocation,
non-goals and provider preservation. No implementation Critical/High/Medium/Low
defects were identified by static review. The successful build and fake-only checks
do not establish production readiness; the adapter/privacy limitations above
prohibit production integration. The foundation is suitable for separate human
source review, with live provider use still deferred.
`git diff --check` passes; new-file whitespace is also checked
separately because ordinary git diff excludes untracked files.
