using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public sealed class ExecutionReconciliationAssessmentRecordConfiguration : IEntityTypeConfiguration<ExecutionReconciliationAssessmentRecord>
{
    public void Configure(EntityTypeBuilder<ExecutionReconciliationAssessmentRecord> builder)
    {
        builder.ToTable("ExecutionReconciliationAssessments", table =>
        {
            table.HasCheckConstraint("CK_ExecutionReconciliationAssessments_Ids",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_ExecutionReconciliationAssessments_ToolId",
                "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
            table.HasCheckConstraint("CK_ExecutionReconciliationAssessments_State",
                "\"ObservedStatus\" = 1 AND \"ObservedVersion\" = 1 AND " +
                "\"Reason\" BETWEEN 0 AND 5 AND \"Disposition\" IN (0, 1)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ToolId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ObservedStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.AssessedAt).IsRequired();
        builder.HasOne<ActionExecutionStateRecord>().WithMany().HasForeignKey(x => x.ActionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionApproval>().WithMany().HasForeignKey(x => x.ApprovalId).OnDelete(DeleteBehavior.Restrict);
    }
}
