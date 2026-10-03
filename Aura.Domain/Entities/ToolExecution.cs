namespace Aura.Domain.Entities;

public class ToolExecution
{
    public Guid Id { get; set; }

    public Guid AIActionId { get; set; }

    public string ToolName { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string? Target { get; set; }

    public string? InputSummary { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int? ExitCode { get; set; }

    public string? OutputSummary { get; set; }

    public string? ErrorSummary { get; set; }

    public DateTime CreatedAt { get; set; }
}