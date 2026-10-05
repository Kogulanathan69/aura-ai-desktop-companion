using Pgvector;
using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class PersonalMemoryConfiguration
    : IEntityTypeConfiguration<PersonalMemory>
{
    public void Configure(EntityTypeBuilder<PersonalMemory> builder)
    {
        builder.ToTable("PersonalMemories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

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
                value => value == null ? null : new Vector(value),
                value => value == null ? null : value.ToArray())
            .HasColumnType("vector(768)")
            .IsRequired(false);

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

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}