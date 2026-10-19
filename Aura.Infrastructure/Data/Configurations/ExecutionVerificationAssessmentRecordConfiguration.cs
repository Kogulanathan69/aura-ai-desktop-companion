using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public sealed class ExecutionVerificationAssessmentRecordConfiguration : IEntityTypeConfiguration<ExecutionVerificationAssessmentRecord>
{
    public void Configure(EntityTypeBuilder<ExecutionVerificationAssessmentRecord> builder)
    {
        builder.ToTable("ExecutionVerificationAssessments", table =>
        {
            table.HasCheckConstraint("CK_ExecutionVerificationAssessments_Ids",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_ExecutionVerificationAssessments_ToolId",
                "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
            table.HasCheckConstraint("CK_ExecutionVerificationAssessments_State",
                "(\"ExecutionStatus\" IN (2, 3) AND \"Outcome\" IN (1, 2) AND " +
                "((\"ExecutionVersion\" IS NULL AND \"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
                "(\"ExecutionVersion\" = 2 AND ((\"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
                "(\"ProofSource\" = 1 AND \"ProofVersion\" = 1))))) OR " +
                "(\"ExecutionStatus\" = 2 AND \"Outcome\" = 0 AND \"ExecutionVersion\" = 2 AND " +
                "\"ProofSource\" = 1 AND \"ProofVersion\" = 1)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ToolId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExecutionStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.ProofSource).HasConversion<int>().IsRequired();
        builder.Property(x => x.AssessedAt).IsRequired();
        builder.HasOne<ActionExecutionStateRecord>().WithMany().HasForeignKey(x => x.ActionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionApproval>().WithMany().HasForeignKey(x => x.ApprovalId).OnDelete(DeleteBehavior.Restrict);
    }
}
