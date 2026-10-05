using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aura.Infrastructure.Data;

public class AuraDbContext : DbContext
{
    public AuraDbContext(DbContextOptions<AuraDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectFile> ProjectFiles => Set<ProjectFile>();
    public DbSet<ProjectSession> ProjectSessions => Set<ProjectSession>();
    public DbSet<ProjectMemory> ProjectMemories => Set<ProjectMemory>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<ProjectDecision> ProjectDecisions => Set<ProjectDecision>();
    public DbSet<ProjectError> ProjectErrors => Set<ProjectError>();
    public DbSet<ErrorSolution> ErrorSolutions => Set<ErrorSolution>();

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();

    public DbSet<AIAction> AIActions => Set<AIAction>();
    public DbSet<ActionApproval> ActionApprovals => Set<ActionApproval>();
    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();
    public DbSet<Verification> Verifications => Set<Verification>();

    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Reminder> Reminders => Set<Reminder>();

    public DbSet<PersonalMemory> PersonalMemories => Set<PersonalMemory>();
}