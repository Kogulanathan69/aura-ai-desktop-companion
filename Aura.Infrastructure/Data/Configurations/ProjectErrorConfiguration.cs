using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class ProjectErrorConfiguration
    : IEntityTypeConfiguration<ProjectError>
{
    public void Configure(EntityTypeBuilder<ProjectError> builder)
    {
        builder.ToTable("ProjectErrors");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProjectId)
            .IsRequired();

        builder.Property(x => x.SessionId)
            .IsRequired(false);

        builder.Property(x => x.FileId)
            .IsRequired(false);

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ErrorType)
            .HasMaxLength(100);

        builder.Property(x => x.ErrorMessage)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Analysis)
            .HasColumnType("text");

        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.FirstSeenAt)
            .IsRequired();

        builder.Property(x => x.ResolvedAt)
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        // Every error belongs to a project.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Session is optional.
        // Keep the error even if the session is removed.
        builder.HasOne<ProjectSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.SetNull);

        // File is optional.
        // Keep the error history even if the file record is removed.
        builder.HasOne<ProjectFile>()
            .WithMany()
            .HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}