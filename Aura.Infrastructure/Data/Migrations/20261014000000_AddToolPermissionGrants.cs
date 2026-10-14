using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aura.Infrastructure.Data.Migrations;

// Targeted additive migration. Existing Permissions rows and tables are untouched.
[DbContext(typeof(AuraDbContext))]
[Migration("20261014000000_AddToolPermissionGrants")]
public sealed class AddToolPermissionGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ToolPermissionGrants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ToolId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RequiredPermission = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ToolPermissionGrants", x => x.Id);
                table.CheckConstraint("CK_ToolPermissionGrants_NonemptyIds",
                    "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("CK_ToolPermissionGrants_ToolId",
                    "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
                table.CheckConstraint("CK_ToolPermissionGrants_RequiredPermission",
                    "\"RequiredPermission\" IN (0, 1, 2)");
                table.CheckConstraint("CK_ToolPermissionGrants_Status",
                    "(\"Status\" = 1 AND \"RevokedAt\" IS NULL) OR " +
                    "(\"Status\" = 2 AND \"RevokedAt\" IS NOT NULL)");
                table.ForeignKey("FK_ToolPermissionGrants_Users_UserId", x => x.UserId,
                    "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ToolPermissionGrants_Conversations_ConversationId", x => x.ConversationId,
                    "Conversations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ToolPermissionGrants_Projects_ProjectId", x => x.ProjectId,
                    "Projects", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_ToolPermissionGrants_UserId", "ToolPermissionGrants", "UserId");
        migrationBuilder.CreateIndex("IX_ToolPermissionGrants_ConversationId", "ToolPermissionGrants", "ConversationId");
        migrationBuilder.CreateIndex("IX_ToolPermissionGrants_ProjectId", "ToolPermissionGrants", "ProjectId");
        migrationBuilder.CreateIndex(
            name: "UX_ToolPermissionGrants_ActiveExactGrant",
            table: "ToolPermissionGrants",
            columns: ["UserId", "ConversationId", "ProjectId", "ToolId", "RequiredPermission"],
            unique: true,
            filter: "\"Status\" = 1 AND \"RevokedAt\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ToolPermissionGrants");
}
