using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aura.Infrastructure.Data.Migrations;

// Source only. Never applied by API startup or the offline checks.
[DbContext(typeof(AuraDbContext))]
[Migration("20261019000000_AddIndependentVerificationOutcome")]
public sealed class AddIndependentVerificationOutcome : Migration
{
    public const string RevisedState =
        "(\"ExecutionStatus\" IN (2, 3) AND \"Outcome\" IN (1, 2) AND " +
        "((\"ExecutionVersion\" IS NULL AND \"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
        "(\"ExecutionVersion\" = 2 AND ((\"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
        "(\"ProofSource\" = 1 AND \"ProofVersion\" = 1))))) OR " +
        "(\"ExecutionStatus\" = 2 AND \"Outcome\" = 0 AND \"ExecutionVersion\" = 2 AND " +
        "\"ProofSource\" = 1 AND \"ProofVersion\" = 1)";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_ExecutionVerificationAssessments_State",
            "ExecutionVerificationAssessments");
        migrationBuilder.AddCheckConstraint("CK_ExecutionVerificationAssessments_State",
            "ExecutionVerificationAssessments", RevisedState);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_ExecutionVerificationAssessments_State",
            "ExecutionVerificationAssessments");
        migrationBuilder.AddCheckConstraint("CK_ExecutionVerificationAssessments_State",
            "ExecutionVerificationAssessments",
            "\"ExecutionStatus\" IN (2, 3) AND \"Outcome\" IN (1, 2) AND " +
            "((\"ExecutionVersion\" IS NULL AND \"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
            "(\"ExecutionVersion\" = 2 AND ((\"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
            "(\"ProofSource\" = 1 AND \"ProofVersion\" = 1))))");
    }
}
