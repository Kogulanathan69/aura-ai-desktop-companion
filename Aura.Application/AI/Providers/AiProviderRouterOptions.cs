namespace Aura.Application.AI.Providers;

public enum AiProviderMode { Local, Cloud }

public sealed class AiProviderRouterOptions
{
    public const string SectionName = "AI:Router";
    public AiProviderMode Mode { get; init; } = AiProviderMode.Local;
}
