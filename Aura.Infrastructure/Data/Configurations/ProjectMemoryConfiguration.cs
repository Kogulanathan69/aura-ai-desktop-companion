using Pgvector;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class ProjectMemoryConfiguration
    : IEntityTypeConfiguration<ProjectMemory>
{
    public void Configure(EntityTypeBuilder<ProjectMemory> builder)
    {
        builder.ToTable("ProjectMemories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProjectId)
            .IsRequired();

        builder.Property(x => x.SessionId)
            .IsRequired(false);

        builder.Property(x => x.Type)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Content)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Embedding)
            .HasConversion(
                value => new Vector(value),
                value => value.ToArray())
            .HasColumnType("vector(768)")
            .IsRequired();

        builder.Property(x => x.Importance)
            .IsRequired();

        builder.Property(x => x.SourceType)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.Property(x => x.LastAccessedAt)
            .IsRequired(false);

        builder.Property(x => x.AccessCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProjectSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}