using Aura.Domain.Entities;

namespace Aura.Application.Verifications;

// Issued only by the authoritative database evidence source. This is a bounded
// observation, not a claim about a handler's response or an authorization grant.
public sealed class OwnedProjectMetadataProof
{
    public Guid ProjectId { get; }
    public long ProjectVersion { get; }
    public DateTime ProjectUpdatedAt { get; }
    public long ExecutionVersion { get; }
    public VerificationProofSource ProofSource => VerificationProofSource.OwnedProjectMetadata;
    public int ProofVersion => 1;

    private OwnedProjectMetadataProof(Guid projectId, long projectVersion, DateTime updatedAt, long executionVersion)
    {
        ProjectId = projectId;
        ProjectVersion = projectVersion;
        ProjectUpdatedAt = updatedAt;
        ExecutionVersion = executionVersion;
    }

    internal static OwnedProjectMetadataProof? FromAuthoritativeRead(Guid projectId,
        DateTime updatedAt, long executionVersion, long projectVersion) =>
        projectId != Guid.Empty && projectVersion > 0 && updatedAt != default &&
        updatedAt.Kind == DateTimeKind.Utc && executionVersion == 2
            ? new(projectId, projectVersion, updatedAt, executionVersion) : null;
}
