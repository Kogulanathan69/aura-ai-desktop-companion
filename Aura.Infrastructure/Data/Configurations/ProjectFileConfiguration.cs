using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class ProjectFileConfiguration : IEntityTypeConfiguration<ProjectFile>
{
    public void Configure(EntityTypeBuilder<ProjectFile> builder)
    {
        builder.ToTable("ProjectFiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProjectId)
            .IsRequired();

        builder.Property(x => x.RelativePath)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Extension)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.ContentHash)
            .HasMaxLength(128);

        builder.Property(x => x.IsIndexed)
            .IsRequired();

        builder.Property(x => x.IsSensitive)
            .IsRequired();

        builder.Property(x => x.LastIndexedAt)
            .IsRequired(false);

        builder.Property(x => x.LastModifiedAt)
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.ProjectId, x.RelativePath })
            .IsUnique();

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}