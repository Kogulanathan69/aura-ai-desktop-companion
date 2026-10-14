using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aura.Infrastructure.Data.Migrations;

[DbContext(typeof(AuraDbContext))]
[Migration("20261014000003_AddSecurityAuditOutbox")]
public sealed class AddSecurityAuditOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SecurityAuditOutbox",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                AuditEventId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                EventType = table.Column<int>(
                    type: "integer",
                    nullable: false),

                Outcome = table.Column<int>(
                    type: "integer",
                    nullable: false),

                OccurredAt = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),

                UserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                ConversationId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                ProjectId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                ActionId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                ToolId = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false),

                ApprovalId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                ExecutionId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                DeliveryState = table.Column<int>(
                    type: "integer",
                    nullable: false),

                AttemptCount = table.Column<int>(
                    type: "integer",
                    nullable: false),

                DeliveredAt = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_SecurityAuditOutbox",
                    x => x.Id);

                table.CheckConstraint(
                    "CK_SecurityAuditOutbox_Ids",
                    "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"AuditEventId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

                table.CheckConstraint(
                    "CK_SecurityAuditOutbox_ToolId",
                    "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");

                table.CheckConstraint(
                    "CK_SecurityAuditOutbox_Enums",
                    "\"EventType\" BETWEEN 0 AND 11 AND " +
                    "\"Outcome\" BETWEEN 0 AND 3 AND " +
                    "\"DeliveryState\" BETWEEN 0 AND 2");

                table.CheckConstraint(
                    "CK_SecurityAuditOutbox_Delivery",
                    "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
                    "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
                    "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0)");

                table.ForeignKey(
                    name: "FK_SecurityAuditOutbox_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);

                table.ForeignKey(
                    name: "FK_SecurityAuditOutbox_Conversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "Conversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);

                table.ForeignKey(
                    name: "FK_SecurityAuditOutbox_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);

                table.ForeignKey(
                    name: "FK_SecurityAuditOutbox_AIActions_ActionId",
                    column: x => x.ActionId,
                    principalTable: "AIActions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);

                table.ForeignKey(
                    name: "FK_SecurityAuditOutbox_ActionApprovals_ApprovalId",
                    column: x => x.ApprovalId,
                    principalTable: "ActionApprovals",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditOutbox_AuditEventId",
            table: "SecurityAuditOutbox",
            column: "AuditEventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditOutbox_ExecutionId",
            table: "SecurityAuditOutbox",
            column: "ExecutionId");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditOutbox_DeliveryState_OccurredAt",
            table: "SecurityAuditOutbox",
            columns: new[]
            {
                "DeliveryState",
                "OccurredAt"
            });

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditOutbox_UserId",
            table: "SecurityAuditOutbox",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditOutbox_ConversationId",
            table: "SecurityAuditOutbox",
            column: "ConversationId");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditOutbox_ProjectId",
            table: "SecurityAuditOutbox",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditOutbox_ActionId",
            table: "SecurityAuditOutbox",
            column: "ActionId");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditOutbox_ApprovalId",
            table: "SecurityAuditOutbox",
            column: "ApprovalId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SecurityAuditOutbox");
    }
}