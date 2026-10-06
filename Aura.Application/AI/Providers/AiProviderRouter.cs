using Aura.Application.AI.DTOs;
using Aura.Application.AI.Interfaces;
using Aura.Application.AI.Models;

namespace Aura.Application.AI.Providers;

public sealed class AiProviderRouter(ILocalAiProvider local, ICloudAiProvider cloud,
    AiProviderRouterOptions options) : IAiProviderRouter
{
    public Task<AiGenerationResult> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return options.Mode switch
        {
            AiProviderMode.Local => local.GenerateAsync(request, cancellationToken),
            AiProviderMode.Cloud => cloud.GenerateAsync(request, cancellationToken),
            _ => Task.FromResult(new AiGenerationResult(AiGenerationStatus.InvalidConfiguration))
        };
    }
}
