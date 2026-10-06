using Aura.Application.AI.DTOs;
using Aura.Application.AI.Models;

namespace Aura.Application.AI.Providers;

public interface IAiProviderRouter
{
    Task<AiGenerationResult> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default);
}
