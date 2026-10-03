namespace Aura.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string MessageType { get; set; } = "Text";

    public string? ModelProvider { get; set; }

    public string? ModelName { get; set; }

    public int? TokenCount { get; set; }

    public DateTime CreatedAt { get; set; }
}