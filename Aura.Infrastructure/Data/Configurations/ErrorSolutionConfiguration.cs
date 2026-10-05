using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class ErrorSolutionConfiguration
    : IEntityTypeConfiguration<ErrorSolution>
{
    public void Configure(EntityTypeBuilder<ErrorSolution> builder)
    {
        builder.ToTable("ErrorSolutions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProjectErrorId)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.WasApplied)
            .IsRequired();

        builder.Property(x => x.VerificationStatus)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.VerificationDetails)
            .HasColumnType("text");

        builder.Property(x => x.AppliedAt)
            .IsRequired(false);

        builder.Property(x => x.VerifiedAt)
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasOne<ProjectError>()
            .WithMany()
            .HasForeignKey(x => x.ProjectErrorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}