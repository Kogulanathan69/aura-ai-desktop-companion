using Aura.Application.AI.DTOs;
using Aura.Application.AI.Models;

namespace Aura.Application.AI.Interfaces;

public interface ILocalAiProvider
{
    Task<AiGenerationResult> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default);
}
