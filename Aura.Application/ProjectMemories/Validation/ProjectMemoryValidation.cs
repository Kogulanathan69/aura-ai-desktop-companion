using Aura.Application.Common.Exceptions;

namespace Aura.Application.ProjectMemories.Validation;

public static class ProjectMemoryValidation
{
    private static readonly string[] Types = ["Context", "Progress", "Decision", "Problem", "Architecture", "Note"];

    public static (string Type, string Title, string Content) Normalize(
        string? type, string? title, string? content, short importance)
    {
        var canonical = Types.FirstOrDefault(value =>
            string.Equals(value, type?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (canonical is null) throw new AppValidationException("Memory type is unsupported.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new AppValidationException("Memory title is required and cannot exceed 200 characters.");
        if (string.IsNullOrWhiteSpace(content)) throw new AppValidationException("Memory content is required.");
        if (importance is < 1 or > 5) throw new AppValidationException("Memory importance must be between 1 and 5.");
        return (canonical, title.Trim(), content.Trim());
    }
}
