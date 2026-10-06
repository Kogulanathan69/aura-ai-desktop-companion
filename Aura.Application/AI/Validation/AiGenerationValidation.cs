using Aura.Application.Common.Exceptions;

namespace Aura.Application.AI.Validation;

public static class AiGenerationValidation
{
    public const int MaximumPromptCharacters = 16_384;

    public static string NormalizePrompt(string? prompt)
    {
        // Bound the original input too, before trimming or serialization allocations.
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaximumPromptCharacters)
            throw new AppValidationException("Prompt is required and cannot exceed 16384 characters.");
        return prompt.Trim();
    }
}
