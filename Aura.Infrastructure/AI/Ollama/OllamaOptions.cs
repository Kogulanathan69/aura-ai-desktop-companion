namespace Aura.Infrastructure.AI.Ollama;

public sealed class OllamaOptions
{
    public const string SectionName = "AI:Ollama";
    public const int MaximumResponseBytes = 1024 * 1024;
    public bool Enabled { get; init; } = false;
    public string BaseUrl { get; init; } = "http://localhost:11434";
    // An explicit administrator-selected local model is required before enablement.
    public string Model { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 60;
}
