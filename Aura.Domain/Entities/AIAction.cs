namespace Aura.Domain.Entities;

public class AIAction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? ConversationId { get; set; }

    public string ActionType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string RiskLevel { get; set; } = "Low";

    public bool RequiresApproval { get; set; }

    public string Status { get; set; } = "Proposed";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}