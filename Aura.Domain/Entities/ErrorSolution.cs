namespace Aura.Domain.Entities;

public class ErrorSolution
{
    public Guid Id { get; set; }

    public Guid ProjectErrorId { get; set; }

    public string Description { get; set; } = string.Empty;

    public bool WasApplied { get; set; }

    public string VerificationStatus { get; set; } = "Pending";

    public string? VerificationDetails { get; set; }

    public DateTime? AppliedAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}