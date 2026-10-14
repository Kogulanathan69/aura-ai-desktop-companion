namespace Aura.Domain.Entities;

// Separate from the generic Permission rows used by project-file access.
// No application service creates grants in this schema-foundation step.
public sealed class ToolPermissionGrantRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ProjectId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    // Numeric values match the bounded Application ToolPermissionRequirement enum.
    // The database constraint is authoritative until a reviewed adapter is added.
    public int RequiredPermission { get; set; }
    public ToolPermissionGrantStatus Status { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime GrantedAt { get; set; }
}

public enum ToolPermissionGrantStatus { Active = 1, Revoked = 2 }
