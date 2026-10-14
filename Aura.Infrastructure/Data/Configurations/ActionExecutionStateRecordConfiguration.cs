using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public sealed class ActionExecutionStateRecordConfiguration : IEntityTypeConfiguration<ActionExecutionStateRecord>
{
    public void Configure(EntityTypeBuilder<ActionExecutionStateRecord> builder)
    {
        builder.ToTable("ActionExecutionStates", table =>
        {
            table.HasCheckConstraint("CK_ActionExecutionStates_NonemptyIds",
                "\"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "(\"ExecutionId\" IS NULL OR \"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
            table.HasCheckConstraint("CK_ActionExecutionStates_ToolId", "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
            table.HasCheckConstraint("CK_ActionExecutionStates_State",
                "(\"Status\" = 0 AND \"Version\" = 0 AND \"ExecutionId\" IS NULL AND NOT \"ApprovalConsumed\" AND \"ReservedAt\" IS NULL AND \"CompletedAt\" IS NULL) OR " +
                "(\"Status\" = 1 AND \"Version\" = 1 AND \"ExecutionId\" IS NOT NULL AND \"ApprovalConsumed\" AND \"ReservedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL) OR " +
                "(\"Status\" IN (2, 3) AND \"Version\" = 2 AND \"ExecutionId\" IS NOT NULL AND \"ApprovalConsumed\" AND \"ReservedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL)");
        });
        builder.HasKey(x => x.ActionId);
        builder.Property(x => x.ToolId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.ApprovalId).IsUnique();
        builder.HasIndex(x => x.ExecutionId).IsUnique().HasFilter("\"ExecutionId\" IS NOT NULL");
        builder.HasOne<AIAction>().WithMany().HasForeignKey(x => x.ActionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionApproval>().WithMany().HasForeignKey(x => x.ApprovalId).OnDelete(DeleteBehavior.Restrict);
    }
}
