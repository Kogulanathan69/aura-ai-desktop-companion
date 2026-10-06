using System.Text;
using Aura.Application.AI.Chat.Validation;
using Aura.Application.Privacy.Interfaces;
using Aura.Application.Privacy.Models;

namespace Aura.Application.ProjectMemories.Retrieval;

public sealed class ProjectMemoryRetriever(IProjectMemoryScopeValidator scopes,
    IProjectMemoryCandidateSource source, IPrivacyGuard privacy, IProjectMemoryTextPolicy textPolicy)
    : IProjectMemoryRetriever
{
    public async Task<IReadOnlyList<RetrievedProjectMemory>> RetrieveAsync(ProjectMemoryScope? scope,
        string normalizedPrompt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = AiChatValidation.NormalizePrompt(normalizedPrompt);
        if (scope is null || scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty ||
            scope.ProjectId == Guid.Empty || !await scopes.ValidateAsync(scope, cancellationToken)) return [];
        var candidates = new List<RetrievedProjectMemory>(MemoryContextLimits.MaximumCandidates);
        // Explicit MoveNext cap avoids even requesting candidate 33 from a lazy adapter.
        await using var iterator = source.RetrieveAsync(scope, prompt,
            MemoryContextLimits.MaximumCandidates, cancellationToken).GetAsyncEnumerator(cancellationToken);
        for (var i = 0; i < MemoryContextLimits.MaximumCandidates; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await iterator.MoveNextAsync()) break;
            var item = iterator.Current;
            if (item is null || item.Id == Guid.Empty || item.ProjectId != scope.ProjectId ||
                !ValidText(item.Content) || !textPolicy.IsApproved(item)) continue;
            // Do not truncate first: truncation could hide credential/private-key evidence.
            // Redact and Block both fail closed; arbitrary sanitized text is not approved text.
            if (privacy.Evaluate(new(null, null, null, item.Content)).Decision != PrivacyDecision.Allow) continue;
            candidates.Add(item);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Recheck ownership after retrieval; adapters cannot authorize provider use themselves.
        if (!await scopes.ValidateAsync(scope, cancellationToken)) return [];
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<RetrievedProjectMemory>(MemoryContextLimits.MaximumMemories);
        var total = 0;
        // Stable ordering independent of adapter ranking. Duplicate/conflicting IDs are omitted.
        foreach (var group in candidates.GroupBy(x => x.Id).OrderBy(x => x.Key))
        {
            if (group.Count() != 1) continue;
            var item = group.Single();
            if (total + item.Content.Length > MemoryContextLimits.MaximumContextCharacters) continue;
            result.Add(item);
            total += item.Content.Length;
            if (result.Count == MemoryContextLimits.MaximumMemories) break;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result.AsReadOnly();
    }

    private static bool ValidText(string? content)
    {
        if (content is null || content.Length > MemoryContextLimits.MaximumMemoryCharacters ||
            string.IsNullOrWhiteSpace(content) || content.Any(c => char.IsControl(c))) return false;
        try { return new UTF8Encoding(false, true).GetByteCount(content) <= MemoryContextLimits.MaximumMemoryBytes; }
        catch (EncoderFallbackException) { return false; }
    }
}
