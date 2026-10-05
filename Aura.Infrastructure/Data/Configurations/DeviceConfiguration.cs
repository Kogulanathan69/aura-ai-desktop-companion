using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public class DeviceConfiguration
    : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.DeviceType)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Platform)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.DeviceIdentifier)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.IsTrusted)
            .IsRequired();

        builder.Property(x => x.CanExecuteTools)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.RegisteredAt)
            .IsRequired();

        builder.Property(x => x.LastSeenAt)
            .IsRequired(false);

        builder.Property(x => x.RevokedAt)
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}