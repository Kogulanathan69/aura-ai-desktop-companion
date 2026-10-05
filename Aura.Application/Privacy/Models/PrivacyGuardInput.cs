namespace Aura.Application.Privacy.Models;

public sealed record PrivacyGuardInput(
    string? RelativePath,
    string? FileName,
    string? Extension,
    string? Content = null);
