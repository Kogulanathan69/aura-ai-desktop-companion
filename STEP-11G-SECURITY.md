# Step 11G: manual project memory foundation

Uses existing ProjectMemory schema unchanged. No automatic memory extraction,
file reads, AI/cloud processing, embedding generation, vector search or new packages.

## Application and API contract

ProjectMemories contains three DTOs, service/interface and application validation.
The scoped service resolves IUserIdentityService for every operation, then checks
Project.Id and local Project.UserId. All memory lookups include that owned ProjectId.
Foreign/missing projects return null (false on delete); foreign/missing memory IDs
have identical not-found behavior. Reads use AsNoTracking and project only safe DTO
fields, excluding stored embeddings. Every async EF operation receives cancellation.

Authenticated API group: /api/projects/{projectId:guid}/memories.
GET / lists; GET /{memoryId:guid} reads; POST / creates; PUT /{memoryId:guid}
updates; DELETE /{memoryId:guid} deletes. Creation returns 201 and a resource location,
reads/updates 200, deletion 204, missing/foreign targets 404. Existing exception handler
maps safe AppValidationException to 400. Authentication/JWT behavior is unchanged.

Type is trimmed and canonicalized case-insensitively to Context, Progress, Decision,
Problem, Architecture, Note. Title is required after trimming and limited to 200
characters. Content is required after trimming. Importance is short, range 1..5,
default 3 in both request DTOs. SessionId is optional and null means no linkage.
A supplied session must match Id, same ProjectId and authenticated local UserId;
missing or foreign sessions receive the same safe validation message. Validation
precedes all mutations. No active-session inference or session content inspection.

Create assigns a new GUID and route ProjectId; SourceType is always Manual. CreatedAt
and UpdatedAt use one server clock value; AccessCount=0 and LastAccessedAt=null.
Update permits only Type, Title, Content, Importance and validated SessionId, updating
UpdatedAt server-side. It never moves ProjectId or changes source, embedding, created
time or access metadata. Delete uses the same ownership/project-scoped boundary.
Lists sort Importance DESC, UpdatedAt DESC, Id DESC. List/detail reads do not mutate
access counters/timestamps. Client DTOs exclude IDs other than optional session linkage,
source and embeddings; unknown JSON members cannot override server-controlled values.
Responses exclude embedding, user IDs and navigation properties.

## Approved no-embedding sentinel

Existing entity defaults Embedding to an empty float array, but the unchanged mapping
requires non-null vector(768). An empty/default vector cannot support PostgreSQL insertion.
The user explicitly approved a server-owned 768-zero placeholder for manual memory
creation. This sentinel means NO EMBEDDING: no semantic model is invoked and no client
embedding is accepted. Updates preserve the stored value. Future semantic processing
must exclude/replace sentinels rather than treating them as generated embeddings.
No entity, EF configuration, schema or migration was changed. Real PostgreSQL insertion
and provider conversion still require integration verification.

## Query pattern

Counts below exclude the standard one-query local-user lookup, and exclude writes:

| Operation | Successful SELECTs | Writes |
| --- | --- | --- |
| List | owned-project existence + one projected list = 2 | none |
| Detail | owned-project existence + one projected scoped lookup = 2 | none |
| Create | owned-project existence = 1; optional session check adds 1 | one SaveChanges insert |
| Update | owned-project existence + scoped tracked lookup = 2; optional session adds 1 | one SaveChanges update |
| Delete | owned-project existence + scoped tracked lookup = 2 | one SaveChanges delete |

Missing/foreign targets short-circuit. Session validation is skipped for null. No
per-memory query and no N+1 pattern. The service is scoped alongside EF context/identity.

## Review findings and verification limits

- Critical: none identified in the new memory service/API.
- High: none identified in the new memory service/API. Existing Step 11F Windows
  filesystem TOCTOU remains a separate High if enabled; its default remains disabled.
- Medium: unbounded full-content list retrieval and no application content-size cap
  can consume substantial resources for large projects/inputs. Pagination, quotas and
  content limits are deferred rather than silently introducing incompatible limits.
- Medium: authorization/session validation and writes occur at separate database
  instants; there is no concurrency token, uninterrupted authorization guarantee,
  or database-level same-project session constraint added here. Concurrent privileged
  ownership changes or deletion require real database tests. Concurrent updates use
  existing last-writer semantics.
- Low: zero-vector sentinels require explicit handling in any future embedding pipeline;
  deterministic ordering does not make the current unbounded list scalable.

Manual memory content is application data, not consent for AI/cloud disclosure. There
are no content logs, arbitrary Privacy Guard file reads, file-access toggles or background
creation paths in this change. Step 11F gates, root policy and all existing checks are intact.

Package-free checks use the real service with in-memory query/write doubles and inspect
endpoint authorization metadata. They cover owned/foreign/cross-project CRUD, session
isolation, input validation, canonical types, sorting, preserved immutable metadata,
sentinel initialization, JSON overposting protection, cancellation and existing 11F gates.
They do not validate EF SQL translation, transactions, PostgreSQL vector conversion or
real HTTP/JWT binding. Run those integration cases before deployment.

Verification result: `dotnet build Aura.slnx --no-restore` passed with zero warnings
and errors. The focused check executable passed all 80 checks (41 memory checks plus
39 existing checks). Its dependency restore/build emitted NU1900 because NuGet
vulnerability metadata was unavailable, including on an escalated restore retry;
dependency restoration itself succeeded. No database migrations or runtime database
writes were executed during verification.

No commits/pushes, migrations/schema changes, package additions, AI/Ollama/OpenAI,
vector retrieval, automatic memory creation or ProjectFileAccess enablement.
