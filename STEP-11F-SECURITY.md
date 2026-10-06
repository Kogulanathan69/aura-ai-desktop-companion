# Step 11F: disabled physical content access foundation

The shipped configuration has no ProjectFileAccess section. Enabled defaults to false.
The API binds one server configuration snapshot at startup and shares it with the
application and infrastructure. No request DTO or endpoint can change it or roots.
Changing server configuration requires restarting the application. No real roots,
user IDs, or enablement settings belong in source control.

## Behavior and security contract

- Disabled API: content route is absent (normal unmatched-route behavior).
- Disabled application: generic Unavailable result before identity, DB, privacy,
  or reader calls.
- Disabled infrastructure: generic Unavailable result before path resolution,
  metadata inspection, or open. Configured roots do not bypass either gate.
- Enabled service: resolve authenticated LOCAL user; query owned project;
  query registered file scoped to project; query effective scoped Read grant;
  normalize registered metadata; reject IsSensitive or metadata Privacy Guard
  Block; invoke reader; requery project/file/permission; evaluate content privacy;
  return only allowed or sanitized text. All DB reads use AsNoTracking and cancellation.
- Permissions require local UserId, ProjectId, ProjectFile resource type, file GUID
  in D format, Read level, Granted status, and null RevokedAt.
- Registered RelativePath, FileName, Extension are immutable through UpdateAsync.
  Incoming and stored tuples use existing Step 11C Normalize and ordinal tuple
  comparison. Trim and slash normalization are accepted; case changes are not
  silently normalized beyond the existing rules. Identity fields are never assigned
  by UpdateAsync. Its obsolete path-change duplicate/revocation branch is removed;
  explicit permission revocation remains intact.
- Changing path/type requires removing the old registration (and its scoped grants),
  registering a new file with a new GUID, and explicitly granting Read again.
  No permissions transfer. Existing update DTO is retained for API compatibility;
  it now permits only identity-equivalent updates. Later API cleanup should remove
  identity fields and expose only genuinely mutable fields, if any; currently there
  are no mutable client fields in that DTO.
- Project.LocalPath is also fixed at creation through normal project updates.
  Creation already stores it verbatim, so updates compare with StringComparison.Ordinal
  without trimming, separator conversion, case folding or filesystem canonicalization.
  Same strings and null-to-null are accepted; any difference, including null/value,
  null/empty, whitespace, slash or case changes, is rejected after ownership lookup
  and before any mutation/save with a generic application validation message.
  No LocalPath assignment occurs in UpdateAsync. Name, Description, RepositoryUrl,
  CurrentBranch and Status remain mutable; UpdatedAt still advances on accepted updates.
  The update DTO retains LocalPath for compatibility; future cleanup should remove
  this identity field and expose only mutable fields. No root re-binding workflow exists.

## Reader policy

Windows only. AllowedRootsByUser is an administrator-controlled dictionary keyed
by authenticated local GUID in D format. Missing/empty roots and any malformed
root for that user fail closed. No inferred roots or fallback. Only fully qualified
drive directory paths are accepted; UNC, device paths, whole-volume authorization,
dot components, empty components, trailing dots/spaces, invalid characters and
reserved Windows device names are rejected.

Canonicalize roots/project/target with Path.GetFullPath. Trim ending root separator.
Project must equal an allowed root or start with that root plus a directory separator
using OrdinalIgnoreCase. Candidate must be strictly below the project using the same
boundary comparison. Construct candidate solely from stored root and normalized,
validated registered path. Reject alternate data streams, rooted/drive-qualified
relative paths and traversal. The actual final extension must be allowed and equal
registered extension ignoring case; an empty or misleading extension fails closed.

Check attributes of the volume and every component through allowed root, project
root and final file. Reject any ReparsePoint and wrong directory/file type, including
stable root junctions, intermediate junctions, and final symlinks. No directory scan.
These are managed path-based checks, NOT a race-free containment mechanism.

Supported extensions: .cs, .csproj, .sln, .slnx, .json, .xml, .config, .md, .txt,
.ts, .tsx, .js, .jsx, .html, .css, .scss, .sql, .py, .dart, .yaml, .yml.

Limit: 1,048,576 bytes. Check FileInfo.Length, then opened stream.Length; allocate
1,048,577 bytes and read only remaining buffer capacity until EOF or full. The extra
byte detects growth beyond the limit. FileShare.Read restricts cooperating writers;
the bound still applies independently of sharing. Decode strict UTF-8, optionally
strip a UTF-8 BOM, reject invalid encoding and any NUL byte. Expected IO/security/path
exceptions return generic UnsafeOrUnreadable; cancellation propagates without content.

IsSensitive=true and metadata Privacy Guard Block prevent physical reader invocation.
IsSensitive=false makes no safety assertion. After reading, requery ownership and
root equality, scoped file existence and all three exact original identity values,
IsSensitive, and effective unrevoked Read. Persistent changes deny return. Then
Privacy Guard Block discards content; Redact returns only RedactedContent (missing
sanitized text fails closed); Allow returns bounded text. Allow means no known rule
matched, never AI/cloud consent. Failure results contain no file metadata or content.

Success: six content-service DB queries plus one local-user lookup (seven total).
Pre-read: project, file, permission; post-read: project, file, permission. Denials
short circuit. Disabled: zero queries. No transaction or uninterrupted authorization
claim. Multiple queries can observe different database instants.

DI: options and stateless reader singleton; content service scoped alongside DB
context/identity; existing Privacy Guard singleton. JWT behavior is unchanged.
No content persistence, logging, audit payloads, hashing, indexing, file modification,
AI/cloud calls, migrations, schema changes, new packages, ACL changes, or native APIs.

## Findings and enablement decision

This is a scoped code review, not an independent penetration-test certification.

- Critical: none identified in this change.
- High #1: Windows filesystem TOCTOU remains when enabled. A path/reparse component
  can change after validation before path-based open, potentially crossing the
  approved boundary. It is unreachable with shipped disabled defaults.
- High #2: FileId-to-different-RelativePath/FileName/Extension approval is resolved
  for the V1 application update workflow by immutable identity and new-ID registration.
  Privileged direct database writes remain outside that guarantee.
- Additional High approval-binding concern: resolved for normal metadata updates by
  making Project.LocalPath immutable. Together with immutable ProjectFile identity,
  Project/File IDs and explicit Read grants cannot silently retarget via normal metadata
  updates. Privileged direct database writes and physical file replacement remain outside
  this application guarantee. Filesystem TOCTOU is unchanged.
- Medium: revalidation provides point-in-time checks only; changes after checks,
  revoke/regrant or metadata change/revert cycles may be missed. No uninterrupted
  authorization, file snapshot, or concurrency guarantee is offered.
- Medium deployment concern: overlapping administrator-configured per-user roots
  can intentionally or accidentally permit access to shared underlying files.
- Medium privacy limitation: deterministic rules do not recognize every secret.
  Allowed text is not certified secret-free and does not imply AI/cloud authorization.
- Low: the legacy ProjectFile update DTO contains identity-only fields, so accepted
  updates are effectively no-ops; the Project update DTO also retains immutable LocalPath.
  Later compatible API cleanup is appropriate.

Suitable to commit and merge as a DISABLED foundation, subject to ordinary review.
Not approved to enable for V1 now: runtime race and database integration tests remain.
Run unelevated; normal reads must never require administrator privileges. Use least
privilege process filesystem permissions and appropriate Windows ACLs for approved
roots. Broadly privileged service-account execution is unsupported. If ACLs cannot
adequately bound files accessible to the process, require stronger Windows handle-based
containment before enablement. This change implements neither native APIs nor ACL edits.

## Verification and tests required before enablement

Run `dotnet build Aura.slnx --no-restore` after restoring existing dependencies.
Run `dotnet run --project security-checks/Aura.SecurityChecks.csproj` for package-free
checks of missing defaults, configured-root disabled gates, conditional endpoint
metadata/authorization, Windows root/traversal/prefix isolation, type/size/encoding
failures, bounded successful reads, and existing privacy rules. The checks use isolated
temporary fixtures and never enable production configuration.

Validation on this change: required solution build passed with zero warnings/errors
after restoring the existing dependency graph; all 39 focused checks passed on Windows.
Package-free query doubles exercise real ProjectService and ProjectFileService updates:
same/null/legacy roots, all root changes, mutable fields, foreign ownership, normalized
file identity and rejected path/name/extension changes. They verify branching and mutations,
not EF SQL translation, database persistence, or concurrency.
No real JWT/PostgreSQL or adversarial reparse-race tests were run.

Before enabling, additionally exercise real HTTP/JWT and PostgreSQL integration:
foreign project/file; absent/revoked grants; all immutable identity changes and
equivalent trim/slash tuples; remove/re-register/new-ID/no grant transfer; sensitive
pre-read with a reader spy; redaction-only HTTP response and blocked PEM; persistent
revocation/root/identity/sensitivity changes during a paused read; post-check timing
and change/revert cycles; safe exception/cancellation and absence of content logs.
Exercise stable allowed/project/intermediate/final junctions/symlinks and adversarial
reparse swaps during validation/open on Windows with production-like ACLs and an
unelevated token. Test concurrent growth beyond 1 MiB, sharing behavior, missing and
malformed root configurations, user isolation, alternate separators/streams/device
paths and sibling prefix collisions. Race tests do not prove managed path checks
race-free; evaluate the residual against actual accessible files and privileges.
