namespace Aura.Application.Privacy.Models;

public sealed record PrivacyGuardResult(
    PrivacyDecision Decision,
    IReadOnlyList<string> Reasons,
    string? RedactedContent = null);
