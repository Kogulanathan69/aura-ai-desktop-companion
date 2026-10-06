namespace Aura.Application.AI.Models;

public enum AiGenerationStatus
{
    Success, Disabled, InvalidConfiguration, Unavailable, TimedOut, InvalidResponse
}

// Content is untrusted text, never permission to execute an action.
public sealed record AiGenerationResult(AiGenerationStatus Status, string? Content = null,
    string? Provider = null, string? Model = null);
