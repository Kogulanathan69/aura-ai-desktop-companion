using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class ActionApprovalConfiguration
    : IEntityTypeConfiguration<ActionApproval>
{
    public void Configure(EntityTypeBuilder<ActionApproval> builder)
    {
        builder.ToTable("ActionApprovals");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AIActionId)
            .IsRequired();

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.Decision)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.RequestedAt)
            .IsRequired();

        builder.Property(x => x.DecidedAt)
            .IsRequired(false);

        builder.Property(x => x.DecisionNote)
            .HasColumnType("text");

        builder.Property(x => x.ApprovalSource)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasOne<AIAction>()
            .WithMany()
            .HasForeignKey(x => x.AIActionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}