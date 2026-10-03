namespace Aura.Domain.Entities;

public class Reminder
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? TaskId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime RemindAt { get; set; }

    public string Status { get; set; } = "Scheduled";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? TriggeredAt { get; set; }
}