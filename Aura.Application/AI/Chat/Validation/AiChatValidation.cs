using System.Text;
using Aura.Application.AI.Models;
using Aura.Application.AI.Validation;
using Aura.Application.Common.Exceptions;

namespace Aura.Application.AI.Chat.Validation;

internal static class AiChatValidation
{
    internal const int MaximumAssistantBytes = 1024 * 1024;

    internal static string NormalizePrompt(string? prompt)
    {
        var normalized = AiGenerationValidation.NormalizePrompt(prompt);
        if (normalized.Contains('\0')) throw new AppValidationException("Prompt contains unsupported text.");
        return normalized;
    }

    internal static bool ValidGeneration(AiGenerationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Content) || result.Content.Length > MaximumAssistantBytes ||
            result.Content.Contains('\0') || !SafeIdentifier(result.Provider, 30) || !SafeIdentifier(result.Model, 100)) return false;
        try { return new UTF8Encoding(false, true).GetByteCount(result.Content) <= MaximumAssistantBytes; }
        catch (EncoderFallbackException) { return false; }
    }

    private static bool SafeIdentifier(string? value, int maximum) => !string.IsNullOrEmpty(value) &&
        value.Length <= maximum && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/');
}
