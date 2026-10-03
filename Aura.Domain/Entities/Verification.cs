namespace Aura.Domain.Entities;

public class Verification
{
    public Guid Id { get; set; }

    public Guid AIActionId { get; set; }

    public Guid? ToolExecutionId { get; set; }

    public string VerificationType { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public string? ExpectedResult { get; set; }

    public string? ActualResult { get; set; }

    public string? Details { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}