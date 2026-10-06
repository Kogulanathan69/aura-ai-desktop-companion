using Aura.Application.AI.DTOs;
using Aura.Application.AI.Models;

namespace Aura.Application.AI.Providers;

public interface ICloudAiProvider
{
    Task<AiGenerationResult> GenerateAsync(
        AiGenerationRequest request,
        CancellationToken cancellationToken = default);
}