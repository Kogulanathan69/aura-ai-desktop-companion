using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class ToolExecutionConfiguration
    : IEntityTypeConfiguration<ToolExecution>
{
    public void Configure(EntityTypeBuilder<ToolExecution> builder)
    {
        builder.ToTable("ToolExecutions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AIActionId)
            .IsRequired();

        builder.Property(x => x.ToolName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Operation)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Target)
            .HasColumnType("text");

        builder.Property(x => x.InputSummary)
            .HasColumnType("text");

        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .IsRequired(false);

        builder.Property(x => x.CompletedAt)
            .IsRequired(false);

        builder.Property(x => x.ExitCode)
            .IsRequired(false);

        builder.Property(x => x.OutputSummary)
            .HasColumnType("text");

        builder.Property(x => x.ErrorSummary)
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasOne<AIAction>()
            .WithMany()
            .HasForeignKey(x => x.AIActionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}