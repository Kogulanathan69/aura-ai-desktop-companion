using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Application.Common.Interfaces;

public interface IAuraDbContext
{
    DbSet<User> Users { get; }

    DbSet<Project> Projects { get; }

    DbSet<ProjectFile> ProjectFiles { get; }

    DbSet<ProjectSession> ProjectSessions { get; }

    DbSet<ProjectMemory> ProjectMemories { get; }

    DbSet<ProjectTask> ProjectTasks { get; }

    DbSet<ProjectDecision> ProjectDecisions { get; }

    DbSet<ProjectError> ProjectErrors { get; }

    DbSet<ErrorSolution> ErrorSolutions { get; }

    DbSet<Conversation> Conversations { get; }

    DbSet<Message> Messages { get; }

    DbSet<AIAction> AIActions { get; }

    DbSet<ActionApproval> ActionApprovals { get; }

    DbSet<ToolExecution> ToolExecutions { get; }

    DbSet<Verification> Verifications { get; }

    DbSet<Permission> Permissions { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<Device> Devices { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<Reminder> Reminders { get; }

    DbSet<PersonalMemory> PersonalMemories { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}