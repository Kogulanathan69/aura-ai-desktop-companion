using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aura.Infrastructure.Data.Migrations;

// Source only: never applied at startup. Nullable for pre-existing advisory rows.
[DbContext(typeof(AuraDbContext))]
[Migration("20261018000000_AddTrustedVerificationFoundation")]
public sealed class AddTrustedVerificationFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ExecutionVersion", table: "ExecutionVerificationAssessments",
            type: "bigint", nullable: true);
        migrationBuilder.DropCheckConstraint(
            name: "CK_ExecutionVerificationAssessments_State",
            table: "ExecutionVerificationAssessments");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ExecutionVerificationAssessments_State",
            table: "ExecutionVerificationAssessments",
            sql: "\"ExecutionStatus\" IN (2, 3) AND \"Outcome\" IN (1, 2) AND " +
                 "((\"ExecutionVersion\" IS NULL AND \"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
                 "(\"ExecutionVersion\" = 2 AND ((\"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
                 "(\"ProofSource\" = 1 AND \"ProofVersion\" = 1))))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_ExecutionVerificationAssessments_State",
            table: "ExecutionVerificationAssessments");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ExecutionVerificationAssessments_State",
            table: "ExecutionVerificationAssessments",
            sql: "\"ExecutionStatus\" IN (2, 3) AND \"Outcome\" IN (1, 2) AND " +
                 "\"ProofSource\" = 0 AND \"ProofVersion\" = 0");
        migrationBuilder.DropColumn(
            name: "ExecutionVersion", table: "ExecutionVerificationAssessments");
    }
}
