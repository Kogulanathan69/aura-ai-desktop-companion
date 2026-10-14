using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public sealed class SecurityAuditOutboxRecordConfiguration
    : IEntityTypeConfiguration<SecurityAuditOutboxRecord>
{
    public void Configure(EntityTypeBuilder<SecurityAuditOutboxRecord> builder)
    {
        builder.ToTable("SecurityAuditOutbox", table =>
        {
            table.HasCheckConstraint(
                "CK_SecurityAuditOutbox_Ids",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"AuditEventId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            table.HasCheckConstraint(
                "CK_SecurityAuditOutbox_ToolId",
                "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");

            table.HasCheckConstraint(
                "CK_SecurityAuditOutbox_Enums",
                "\"EventType\" BETWEEN 0 AND 11 AND " +
                "\"Outcome\" BETWEEN 0 AND 3 AND " +
                "\"DeliveryState\" BETWEEN 0 AND 2");

            table.HasCheckConstraint(
                "CK_SecurityAuditOutbox_Delivery",
                "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
                "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
                "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ToolId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.Property(x => x.DeliveryState)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.AttemptCount)
            .IsRequired();

        builder.HasIndex(x => x.AuditEventId)
            .IsUnique();

        builder.HasIndex(x => x.ExecutionId);

        builder.HasIndex(x => new
        {
            x.DeliveryState,
            x.OccurredAt
        });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AIAction>()
            .WithMany()
            .HasForeignKey(x => x.ActionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ActionApproval>()
            .WithMany()
            .HasForeignKey(x => x.ApprovalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}