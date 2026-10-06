using Aura.Application.Common.Exceptions;

namespace Aura.Application.Conversations.Validation;

public static class ConversationValidation
{
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new AppValidationException("Conversation title is required and cannot exceed 200 characters.");
        return title.Trim();
    }

    public static string NormalizeType(string? type, Guid? projectId)
    {
        var canonical = new[] { "General", "Project" }.FirstOrDefault(value =>
            string.Equals(value, type?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (canonical is null || (canonical == "General" && projectId.HasValue) ||
            (canonical == "Project" && !projectId.HasValue))
            throw new AppValidationException("Conversation type and project must form a valid scope.");
        return canonical;
    }

    public static string NormalizeContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new AppValidationException("Message content is required.");
        return content.Trim();
    }
}
