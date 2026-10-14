using Aura.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aura.Infrastructure.Data.Configurations;

public sealed class ToolPermissionGrantRecordConfiguration : IEntityTypeConfiguration<ToolPermissionGrantRecord>
{
    public void Configure(EntityTypeBuilder<ToolPermissionGrantRecord> builder)
    {
        builder.ToTable("ToolPermissionGrants", table =>
        {
            table.HasCheckConstraint("CK_ToolPermissionGrants_NonemptyIds",
                "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_ToolPermissionGrants_ToolId",
                "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
            table.HasCheckConstraint("CK_ToolPermissionGrants_RequiredPermission",
                "\"RequiredPermission\" IN (0, 1, 2)");
            table.HasCheckConstraint("CK_ToolPermissionGrants_Status",
                "(\"Status\" = 1 AND \"RevokedAt\" IS NULL) OR " +
                "(\"Status\" = 2 AND \"RevokedAt\" IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.ConversationId).IsRequired();
        builder.Property(x => x.ProjectId).IsRequired();
        builder.Property(x => x.ToolId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RequiredPermission).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.RevokedAt).IsRequired(false);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.GrantedAt).IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserId, x.ConversationId, x.ProjectId,
            x.ToolId, x.RequiredPermission })
            .IsUnique()
            .HasFilter("\"Status\" = 1 AND \"RevokedAt\" IS NULL")
            .HasDatabaseName("UX_ToolPermissionGrants_ActiveExactGrant");
    }
}
