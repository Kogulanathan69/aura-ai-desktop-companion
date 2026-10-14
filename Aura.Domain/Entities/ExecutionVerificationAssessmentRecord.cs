namespace Aura.Domain.Entities;

// Schema only. No production evidence writer or independent proof provider exists.
public sealed class ExecutionVerificationAssessmentRecord
{
    public Guid Id { get; set; }
    public Guid ActionId { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ProjectId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public Guid ApprovalId { get; set; }
    public Guid ExecutionId { get; set; }
    public DurableExecutionStatus ExecutionStatus { get; set; }
    public int Outcome { get; set; }
    public VerificationProofSource ProofSource { get; set; }
    public int ProofVersion { get; set; }
    public DateTime AssessedAt { get; set; }
}

public enum VerificationProofSource { None = 0 }
