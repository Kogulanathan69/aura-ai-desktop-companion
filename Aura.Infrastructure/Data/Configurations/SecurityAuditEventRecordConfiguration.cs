using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public sealed class SecurityAuditEventRecordConfiguration : IEntityTypeConfiguration<SecurityAuditEventRecord>
{
    public void Configure(EntityTypeBuilder<SecurityAuditEventRecord> builder)
    {
        builder.ToTable("SecurityAuditEvents", table =>
        {
            table.HasCheckConstraint("CK_SecurityAuditEvents_Ids",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "(\"ActionId\" IS NULL OR \"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                "(\"ApprovalId\" IS NULL OR \"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                "(\"ExecutionId\" IS NULL OR \"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                "(\"VerificationId\" IS NULL OR \"VerificationId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                "(\"ReconciliationId\" IS NULL OR \"ReconciliationId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                "(\"PermissionGrantId\" IS NULL OR \"PermissionGrantId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
            table.HasCheckConstraint("CK_SecurityAuditEvents_ToolId",
                "\"ToolId\" IS NULL OR \"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
            table.HasCheckConstraint("CK_SecurityAuditEvents_Enums",
                "\"EventType\" BETWEEN 0 AND 11 AND \"Outcome\" BETWEEN 0 AND 3");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ToolId).HasMaxLength(64).IsRequired(false);
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
