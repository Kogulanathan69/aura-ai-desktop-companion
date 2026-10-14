using Aura.Api.Security;
using Aura.Application.Security;
using Aura.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class AuditOutboxSchemaHardeningChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        const string oldSql =
            "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
            "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
            "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0)";
        const string newSql =
            "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
            "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
            "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NULL)";

        var original = new AddSecurityAuditOutbox();
        var historical = ((CreateTableOperation)original.UpOperations.Single(x => x is CreateTableOperation))
            .CheckConstraints.Single(x => x.Name == "CK_SecurityAuditOutbox_Delivery");
        check(historical.Sql == oldSql,
            "13J historical Step 13G migration retains its original weaker constraint");

        var migration = new HardenSecurityAuditOutboxDelivery();
        check(ExactSwap(migration.UpOperations, newSql),
            "13J Up drops and adds only the named delivery check with the exact new invariant");
        check(ExactSwap(migration.DownOperations, oldSql),
            "13J Down restores the historical Step 13G delivery check exactly");
        check(migration.UpOperations.Count == 2 && migration.DownOperations.Count == 2 &&
            migration.UpOperations.All(x => x is DropCheckConstraintOperation or AddCheckConstraintOperation) &&
            migration.DownOperations.All(x => x is DropCheckConstraintOperation or AddCheckConstraintOperation),
            "13J migration has no column, schema, data, index or unrelated-table mutation");

        var root = Directory.GetCurrentDirectory();
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var dispatcher = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Services",
            "SecurityAuditOutboxDispatcher.cs"));
        check(!api.Contains("SecurityAuditOutboxDispatcher", StringComparison.Ordinal) &&
            !api.Contains("LiveAuditOutboxVerification", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("Database.Migrate(", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal),
            "13J production API has no dispatcher, verifier, hosted worker or auto migration");
        check(!dispatcher.Contains("PeriodicTimer", StringComparison.Ordinal) &&
            !dispatcher.Contains("Task.Delay", StringComparison.Ordinal) &&
            !dispatcher.Contains("while (", StringComparison.Ordinal),
            "13J dispatcher remains explicit one-item delivery without polling or retry");
        var readiness = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var safety = new RuntimeSafetyOptions();
        check(!readiness.AuditSink && !readiness.ExecutionState &&
            !safety.ProductionAdaptersEnabled && !safety.ToolExecutionEnabled &&
            !safety.SafeFileAccessEnabled,
            "13J runtime readiness and dangerous capabilities remain disabled");
        return Task.CompletedTask;
    }

    private static bool ExactSwap(IReadOnlyList<MigrationOperation> operations, string sql) =>
        operations.Count == 2 &&
        operations[0] is DropCheckConstraintOperation drop &&
        drop.Name == "CK_SecurityAuditOutbox_Delivery" && drop.Table == "SecurityAuditOutbox" &&
        operations[1] is AddCheckConstraintOperation add &&
        add.Name == "CK_SecurityAuditOutbox_Delivery" && add.Table == "SecurityAuditOutbox" &&
        add.Sql == sql;
}
