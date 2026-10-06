namespace Aura.Application.ProjectMemories.Retrieval;

public static class MemoryContextLimits
{
    public const int MaximumCandidates = 32;
    public const int MaximumMemories = 4;
    public const int MaximumMemoryCharacters = 512;
    public const int MaximumMemoryBytes = 2048;
    public const int MaximumContextCharacters = 2048;
    public const int MaximumAssembledPromptCharacters = 16384;
}

// Internal application contracts only: never bind these records from an HTTP request.
public sealed record ProjectMemoryScope(Guid UserId, Guid ConversationId, Guid ProjectId);
public sealed record RetrievedProjectMemory(Guid Id, Guid ProjectId, string Content);

public interface IProjectMemoryScopeValidator
{
    // Must freshly verify authenticated identity, owned Project conversation and owned project,
    // using the same rules as OwnedConversations. No caller-supplied scope is authoritative.
    Task<bool> ValidateAsync(ProjectMemoryScope scope, CancellationToken cancellationToken);
}

public interface IProjectMemoryCandidateSource
{
    // Future semantic adapter: scope the query BEFORE ranking/limiting; project bounded DTOs,
    // never embeddings/entities. Yield at most maximumCandidates with bounded content.
    // The consumer independently caps enumeration and rejects adapter contract violations.
    IAsyncEnumerable<RetrievedProjectMemory?> RetrieveAsync(ProjectMemoryScope scope,
        string normalizedPrompt, int maximumCandidates, CancellationToken cancellationToken);
}

public interface IProjectMemoryTextPolicy
{
    // Allow only content with independently established safe provenance/approval.
    // Pattern detection alone cannot establish safety of arbitrary memory text.
    bool IsApproved(RetrievedProjectMemory memory);
}

public sealed class DenyProjectMemoryTextPolicy : IProjectMemoryTextPolicy
{
    public bool IsApproved(RetrievedProjectMemory memory) => false;
}

public interface IProjectMemoryRetriever
{
    Task<IReadOnlyList<RetrievedProjectMemory>> RetrieveAsync(ProjectMemoryScope? scope,
        string normalizedPrompt, CancellationToken cancellationToken = default);
}
