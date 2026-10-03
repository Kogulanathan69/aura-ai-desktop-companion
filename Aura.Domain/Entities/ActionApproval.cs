namespace Aura.Domain.Entities;

public class ActionApproval
{
    public Guid Id { get; set; }

    public Guid AIActionId { get; set; }

    public Guid UserId { get; set; }

    public string Decision { get; set; } = "Pending";

    public DateTime RequestedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    public string ApprovalSource { get; set; } = string.Empty;

    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}