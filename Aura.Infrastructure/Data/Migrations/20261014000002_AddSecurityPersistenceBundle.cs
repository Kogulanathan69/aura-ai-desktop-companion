using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aura.Infrastructure.Data.Migrations;

// Targeted additive migration; historical baseline/snapshot review is still required.
[DbContext(typeof(AuraDbContext))]
[Migration("20261014000002_AddSecurityPersistenceBundle")]
public sealed class AddSecurityPersistenceBundle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SecurityAuditEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<int>(type: "integer", nullable: false),
                Outcome = table.Column<int>(type: "integer", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ActionId = table.Column<Guid>(type: "uuid", nullable: true),
                ToolId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ApprovalId = table.Column<Guid>(type: "uuid", nullable: true),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                VerificationId = table.Column<Guid>(type: "uuid", nullable: true),
                ReconciliationId = table.Column<Guid>(type: "uuid", nullable: true),
                PermissionGrantId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SecurityAuditEvents", x => x.Id);
                table.CheckConstraint("CK_SecurityAuditEvents_Ids",
                    "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "(\"ActionId\" IS NULL OR \"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                    "(\"ApprovalId\" IS NULL OR \"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                    "(\"ExecutionId\" IS NULL OR \"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                    "(\"VerificationId\" IS NULL OR \"VerificationId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                    "(\"ReconciliationId\" IS NULL OR \"ReconciliationId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND " +
                    "(\"PermissionGrantId\" IS NULL OR \"PermissionGrantId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                table.CheckConstraint("CK_SecurityAuditEvents_ToolId",
                    "\"ToolId\" IS NULL OR \"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
                table.CheckConstraint("CK_SecurityAuditEvents_Enums",
                    "\"EventType\" BETWEEN 0 AND 11 AND \"Outcome\" BETWEEN 0 AND 3");
                table.ForeignKey("FK_SecurityAuditEvents_Users_UserId", x => x.UserId,
                    "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SecurityAuditEvents_Conversations_ConversationId", x => x.ConversationId,
                    "Conversations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SecurityAuditEvents_Projects_ProjectId", x => x.ProjectId,
                    "Projects", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_SecurityAuditEvents_UserId", "SecurityAuditEvents", "UserId");
        migrationBuilder.CreateIndex("IX_SecurityAuditEvents_ConversationId", "SecurityAuditEvents", "ConversationId");
        migrationBuilder.CreateIndex("IX_SecurityAuditEvents_ProjectId", "SecurityAuditEvents", "ProjectId");

        migrationBuilder.CreateTable(
            name: "ExecutionReconciliationAssessments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ToolId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ApprovalId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                ObservedStatus = table.Column<int>(type: "integer", nullable: false),
                ObservedVersion = table.Column<long>(type: "bigint", nullable: false),
                Reason = table.Column<int>(type: "integer", nullable: false),
                Disposition = table.Column<int>(type: "integer", nullable: false),
                AssessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExecutionReconciliationAssessments", x => x.Id);
                table.CheckConstraint("CK_ExecutionReconciliationAssessments_Ids",
                    "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("CK_ExecutionReconciliationAssessments_ToolId",
                    "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
                table.CheckConstraint("CK_ExecutionReconciliationAssessments_State",
                    "\"ObservedStatus\" = 1 AND \"ObservedVersion\" = 1 AND " +
                    "\"Reason\" BETWEEN 0 AND 5 AND \"Disposition\" IN (0, 1)");
                table.ForeignKey("FK_ExecutionReconciliationAssessments_ActionExecutionStates_ActionId", x => x.ActionId,
                    "ActionExecutionStates", "ActionId", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ExecutionReconciliationAssessments_Users_UserId", x => x.UserId,
                    "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ExecutionReconciliationAssessments_Conversations_ConversationId", x => x.ConversationId,
                    "Conversations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ExecutionReconciliationAssessments_Projects_ProjectId", x => x.ProjectId,
                    "Projects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ExecutionReconciliationAssessments_ActionApprovals_ApprovalId", x => x.ApprovalId,
                    "ActionApprovals", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ExecutionReconciliationAssessments_ActionId", "ExecutionReconciliationAssessments", "ActionId");
        migrationBuilder.CreateIndex("IX_ExecutionReconciliationAssessments_UserId", "ExecutionReconciliationAssessments", "UserId");
        migrationBuilder.CreateIndex("IX_ExecutionReconciliationAssessments_ConversationId", "ExecutionReconciliationAssessments", "ConversationId");
        migrationBuilder.CreateIndex("IX_ExecutionReconciliationAssessments_ProjectId", "ExecutionReconciliationAssessments", "ProjectId");
        migrationBuilder.CreateIndex("IX_ExecutionReconciliationAssessments_ApprovalId", "ExecutionReconciliationAssessments", "ApprovalId");

        migrationBuilder.CreateTable(
            name: "ExecutionVerificationAssessments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ToolId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ApprovalId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutionStatus = table.Column<int>(type: "integer", nullable: false),
                Outcome = table.Column<int>(type: "integer", nullable: false),
                ProofSource = table.Column<int>(type: "integer", nullable: false),
                ProofVersion = table.Column<int>(type: "integer", nullable: false),
                AssessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExecutionVerificationAssessments", x => x.Id);
                table.CheckConstraint("CK_ExecutionVerificationAssessments_Ids",
                    "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ActionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ConversationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ApprovalId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND " +
                    "\"ExecutionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("CK_ExecutionVerificationAssessments_ToolId",
                    "\"ToolId\" ~ '^[a-z][a-z0-9-]{0,63}$'");
                table.CheckConstraint("CK_ExecutionVerificationAssessments_State",
                    "\"ExecutionStatus\" IN (2, 3) AND \"Outcome\" IN (1, 2) AND " +
                    "\"ProofSource\" = 0 AND \"ProofVersion\" = 0");
                table.ForeignKey("FK_ExecutionVerificationAssessments_ActionExecutionStates_ActionId", x => x.ActionId,
                    "ActionExecutionStates", "ActionId", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ExecutionVerificationAssessments_Users_UserId", x => x.UserId,
                    "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ExecutionVerificationAssessments_Conversations_ConversationId", x => x.ConversationId,
                    "Conversations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ExecutionVerificationAssessments_Projects_ProjectId", x => x.ProjectId,
                    "Projects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ExecutionVerificationAssessments_ActionApprovals_ApprovalId", x => x.ApprovalId,
                    "ActionApprovals", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ExecutionVerificationAssessments_ActionId", "ExecutionVerificationAssessments", "ActionId");
        migrationBuilder.CreateIndex("IX_ExecutionVerificationAssessments_UserId", "ExecutionVerificationAssessments", "UserId");
        migrationBuilder.CreateIndex("IX_ExecutionVerificationAssessments_ConversationId", "ExecutionVerificationAssessments", "ConversationId");
        migrationBuilder.CreateIndex("IX_ExecutionVerificationAssessments_ProjectId", "ExecutionVerificationAssessments", "ProjectId");
        migrationBuilder.CreateIndex("IX_ExecutionVerificationAssessments_ApprovalId", "ExecutionVerificationAssessments", "ApprovalId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ExecutionVerificationAssessments");
        migrationBuilder.DropTable(name: "ExecutionReconciliationAssessments");
        migrationBuilder.DropTable(name: "SecurityAuditEvents");
    }
}
