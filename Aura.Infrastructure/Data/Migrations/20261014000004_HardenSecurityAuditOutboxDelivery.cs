using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aura.Infrastructure.Data.Migrations;

// Constraint-only follow-up. Existing invalid Failed rows must be reviewed
// before applying this migration; no data repair occurs here.
[DbContext(typeof(AuraDbContext))]
[Migration("20261014000004_HardenSecurityAuditOutboxDelivery")]
public sealed class HardenSecurityAuditOutboxDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_SecurityAuditOutbox_Delivery",
            table: "SecurityAuditOutbox");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SecurityAuditOutbox_Delivery",
            table: "SecurityAuditOutbox",
            sql: "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
                 "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
                 "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_SecurityAuditOutbox_Delivery",
            table: "SecurityAuditOutbox");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SecurityAuditOutbox_Delivery",
            table: "SecurityAuditOutbox",
            sql: "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
                 "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
                 "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0)");
    }
}
