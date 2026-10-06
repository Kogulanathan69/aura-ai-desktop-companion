namespace Aura.Infrastructure.AI.OpenAI;

public sealed class OpenAiOptions
{
    public const string SectionName = "AI:OpenAI";

    public const int MaximumResponseBytes = 1024 * 1024;

    public bool Enabled { get; init; } = false;

    public string ApiKey { get; init; } = string.Empty;

    // Explicit administrator-selected OpenAI model.
    public string Model { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 60;
}