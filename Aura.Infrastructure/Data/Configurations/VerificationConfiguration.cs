using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class VerificationConfiguration
    : IEntityTypeConfiguration<Verification>
{
    public void Configure(EntityTypeBuilder<Verification> builder)
    {
        builder.ToTable("Verifications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AIActionId)
            .IsRequired();

        builder.Property(x => x.ToolExecutionId)
            .IsRequired(false);

        builder.Property(x => x.VerificationType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.ExpectedResult)
            .HasColumnType("text");

        builder.Property(x => x.ActualResult)
            .HasColumnType("text");

        builder.Property(x => x.Details)
            .HasColumnType("text");

        builder.Property(x => x.StartedAt)
            .IsRequired(false);

        builder.Property(x => x.CompletedAt)
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasOne<AIAction>()
            .WithMany()
            .HasForeignKey(x => x.AIActionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ToolExecution>()
            .WithMany()
            .HasForeignKey(x => x.ToolExecutionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}