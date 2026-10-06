# Step 11H: conversation and user-message foundation

Uses existing Conversation/Message entities and EF schema unchanged. Adds scoped
IConversationService, DTOs, validation, authenticated endpoints and package-free checks.
No AI replies, file content reads, embeddings, cloud AI or automatic memory creation.

## Public contract

GET /api/conversations lists owned conversations (including archived).
GET /api/conversations/{conversationId:guid} returns one owned conversation.
POST /api/conversations creates a General or Project conversation.
PUT /api/conversations/{conversationId:guid} updates Title and IsArchived only.
GET /api/conversations/{conversationId:guid}/messages lists exact-parent history.
GET /api/conversations/{conversationId:guid}/messages/{messageId:guid} reads one message.
POST /api/conversations/{conversationId:guid}/messages creates a user text message.

All seven routes inherit RequireAuthorization. There is no hard-delete route; archive
and unarchive use PUT IsArchived. No public message update/delete pathway exists.
Archiving is an organizational flag, not an authorization boundary: owned archived
conversations remain readable and accept user messages. Existing API exception handling
returns safe validation errors; foreign/missing resources share 404 behavior.

Every operation resolves authenticated LOCAL identity. OwnedConversations filters
Conversation.UserId and consistent scope: General has null ProjectId; Project has a
non-null ProjectId whose Project.UserId matches that same local identity. The project
ownership EXISTS is part of the conversation query, including lists; no per-row query.
Message operations first establish owned parent scope, then filter exact ConversationId;
individual messages also filter MessageId. Create validates any project link before save.

Conversation create accepts Title, Type (default General), optional ProjectId. Title
is trimmed, required and <=200 characters. Type is trimmed and canonicalized to General
or Project using case-insensitive input comparison. Inconsistent links fail validation.
Update accepts only Title/IsArchived: Id, UserId, CreatedAt, Type, ProjectId, Summary,
LastMessageAt are never assigned from public input. Unknown JSON scope/identity fields
cannot move the record. No root or conversation re-binding workflow is added.

Summary starts null and is server-owned. Public updates preserve it; no summarizer
or manual summary API exists. Responses omit UserId/AuthUserId.

Message create accepts only Content. Trim and require non-whitespace content.
Role=User, MessageType=Text, model provider/name/token count=null. Server GUID and
route-owned parent are assigned internally. Responses use existing safe model fields.
No assistant/system/tool messages are created. Reads do not mutate any metadata.
Stored content is private application data and is not consent for AI/cloud processing.

## Timestamps, queries and persistence

Conversation create sets CreatedAt/UpdatedAt to one IDateTimeProvider.UtcNow value,
LastMessageAt=null, IsArchived=false. Metadata update sets UpdatedAt server-side.
Message create captures one time for Message.CreatedAt, Conversation.LastMessageAt
and Conversation.UpdatedAt. Insert and parent changes use the same scoped DbContext
and exactly one SaveChangesAsync, following existing default relational EF/Npgsql
transaction semantics. No AutoTransactionBehavior override or explicit transaction
infrastructure was introduced. Real transaction rollback/atomicity was not tested.

Conversation list: LastMessageAt.HasValue DESC, LastMessageAt DESC, UpdatedAt DESC,
Id DESC. Explicit HasValue puts nulls last independently of PostgreSQL's default null
ordering. Message list: CreatedAt ASC, Id ASC. Ordering stays within IQueryable and
read queries use AsNoTracking. All async EF calls receive CancellationToken.

Successful SELECT counts, excluding standard one-query local-user resolution:

| Operation | SELECTs | Saves |
| --- | --- | --- |
| Conversation list/detail | 1 (including project ownership EXISTS) | 0 |
| General creation | 0 | 1 insert |
| Project creation | 1 owned-project check | 1 insert |
| Conversation update/archive | 1 scoped tracked lookup | 1 update |
| Message list/detail | 1 parent check + 1 scoped message query | 0 |
| Message creation | 1 scoped tracked parent lookup | 1 insert plus parent update |

Missing targets short circuit. No per-conversation/per-message lookup and no N+1.
ConversationService is scoped alongside DB context and identity. Existing JWT, privacy,
Step 11F options/gates, ProjectMemory behavior and package dependencies are unchanged.

## Review findings

- Critical: none identified in the new service/API.
- High: none identified in the new service/API. Existing Windows filesystem TOCTOU
  remains a separate Step 11F High when physical access is enabled; default remains false.
- Medium: unbounded conversation/message lists and text size may consume substantial
  resources. Pagination, input caps, quotas and rate limits are deferred. No schema change.
- Medium: concurrent message saves can write older LastMessageAt/UpdatedAt after newer
  saves, even though both messages persist. One SaveChanges unit does not serialize
  concurrent requests or make timestamps monotonic. History sorting remains deterministic.
- Medium: parent authorization reads and subsequent message reads/writes occur at
  separate instants; no uninterrupted authorization or concurrency/version guarantee.
  Privileged ownership changes and concurrent project deletion require integration tests.
- Medium lifecycle limitation: existing project FK uses SetNull on deletion. A Project
  conversation can therefore lose its link while retaining Type=Project. This foundation
  fails closed for inconsistent scope and hides that conversation/history from normal API
  operations; it does not silently reclassify it as General. Recovery/deletion policy is
  deferred; no schema/cascade behavior was changed.
- Low: archive is organizational only and does not prevent new messages. A future read-only
  archive policy would need deliberate implementation and compatibility review.

No content logging is introduced. No filesystem, tool, memory creation, AI provider or
embedding dependency exists in ConversationService. Test fixtures exercise physical
files only through the pre-existing isolated 11F checks, never through Step 11H.
No secrets, real user IDs, machine-specific root config, new packages or migrations.

## Verification

Required `dotnet build Aura.slnx --no-restore`: passed with zero warnings/errors.
Package-free focused checks: 115 passed (35 new conversation checks plus 80 existing).
Checks invoke the real service with query/write doubles; verify owned/foreign scopes,
project consistency, immutable scope and overposting, archive/unarchive, content rules,
exact-parent messages, user-only roles, null model fields, timestamps and one save call,
sorting, current project ownership and all endpoint authorization metadata.

NU1900 persists in the check project because NuGet vulnerability metadata was unreachable;
this is separate from passing compilation/checks. Vulnerability scanning is not verified.
No PostgreSQL, HTTP/JWT, provider SQL translation, transactional rollback or concurrent
message runtime tests were performed. Run those before deployment, including timestamp
races and project deletion/ownership transitions. Query-double success does not prove them.

No commits, pushes, schema changes, migrations, packages, AI/Ollama/OpenAI/EmbeddingGemma,
vector search, automatic memories, tool execution or ProjectFileAccess enablement.
