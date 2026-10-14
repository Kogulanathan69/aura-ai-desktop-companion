using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aura.Infrastructure.Data.Migrations;

// Targeted additive migration; historical baseline/snapshot review remains required.
[DbContext(typeof(AuraDbContext))]
[Migration("20261014000001_AddActionExecutionStates")]
public sealed class AddActionExecutionStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ActionExecutionStates",
            columns: table => new
            {
                ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ToolId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ApprovalId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                ApprovalConsumed = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActionExecutionStates", x => x.ActionId);
                table.CheckConstraint("CK_ActionExecutionStates_NonemptyIds",
                    "\"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "(\"ExecutionId\" IS NULL OR \"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                table.CheckConstraint("CK_ActionExecutionStates_ToolId", "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
                table.CheckConstraint("CK_ActionExecutionStates_State",
                    "(\"Status\" = 0 AND \"Version\" = 0 AND \"ExecutionId\" IS NULL AND NOT \"ApprovalConsumed\" AND \"ReservedAt\" IS NULL AND \"CompletedAt\" IS NULL) OR " +
                    "(\"Status\" = 1 AND \"Version\" = 1 AND \"ExecutionId\" IS NOT NULL AND \"ApprovalConsumed\" AND \"ReservedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL) OR " +
                    "(\"Status\" IN (2, 3) AND \"Version\" = 2 AND \"ExecutionId\" IS NOT NULL AND \"ApprovalConsumed\" AND \"ReservedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL)");
                table.ForeignKey("FK_ActionExecutionStates_AIActions_ActionId", x => x.ActionId,
                    "AIActions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ActionExecutionStates_Users_UserId", x => x.UserId,
                    "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ActionExecutionStates_Conversations_ConversationId", x => x.ConversationId,
                    "Conversations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ActionExecutionStates_Projects_ProjectId", x => x.ProjectId,
                    "Projects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ActionExecutionStates_ActionApprovals_ApprovalId", x => x.ApprovalId,
                    "ActionApprovals", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ActionExecutionStates_UserId", "ActionExecutionStates", "UserId");
        migrationBuilder.CreateIndex("IX_ActionExecutionStates_ConversationId", "ActionExecutionStates", "ConversationId");
        migrationBuilder.CreateIndex("IX_ActionExecutionStates_ProjectId", "ActionExecutionStates", "ProjectId");
        migrationBuilder.CreateIndex("IX_ActionExecutionStates_ApprovalId", "ActionExecutionStates", "ApprovalId", unique: true);
        migrationBuilder.CreateIndex("IX_ActionExecutionStates_ExecutionId", "ActionExecutionStates", "ExecutionId",
            unique: true, filter: "\"ExecutionId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ActionExecutionStates");
}
