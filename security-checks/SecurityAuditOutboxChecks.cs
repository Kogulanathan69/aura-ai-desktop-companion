using Aura.Application.Auditing;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class SecurityAuditOutboxChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=metadata_only",
                npgsql => npgsql.UseVector())
            .Options;

        await using var db = new AuraDbContext(options);

        var model = db.GetService<IDesignTimeModel>().Model;

        var outbox =
            model.FindEntityType(typeof(SecurityAuditOutboxRecord))!;

        check(
            outbox.GetTableName() == "SecurityAuditOutbox" &&
            db.SecurityAuditOutbox is not null,
            "13G dedicated security audit outbox table is mapped");

        check(
            outbox.FindPrimaryKey()!
                .Properties
                .Single()
                .Name == "Id",
            "13G audit outbox has exact primary key");

        var fields = outbox.GetProperties()
            .Select(x => x.Name)
            .ToHashSet();

        check(
            fields.SetEquals(new[]
            {
                "Id",
                "AuditEventId",
                "EventType",
                "Outcome",
                "OccurredAt",
                "UserId",
                "ConversationId",
                "ProjectId",
                "ActionId",
                "ToolId",
                "ApprovalId",
                "ExecutionId",
                "DeliveryState",
                "AttemptCount",
                "DeliveredAt"
            }),
            "13G audit outbox carries only bounded execution audit fields");

        check(
            outbox.GetIndexes().Any(index =>
                index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name == "AuditEventId"),
            "13G audit event identity is unique");

        check(
            outbox.FindProperty("ToolId")!
                .GetMaxLength() == 64,
            "13G audit outbox tool identifier is bounded");

        check(
            outbox.FindProperty("DeliveryState")!
                .ClrType == typeof(SecurityAuditOutboxDeliveryState),
            "13G audit delivery state uses bounded enum");

        check(
            (int)SecurityAuditOutboxDeliveryState.Pending == 0 &&
            (int)SecurityAuditOutboxDeliveryState.Delivered == 1 &&
            (int)SecurityAuditOutboxDeliveryState.Failed == 2,
            "13G outbox delivery state numeric values are stable");

        check(
            (int)AuditEventType.ExecutionReserved == 6 &&
            (int)AuditEventType.ExecutionCompleted == 7 &&
            (int)AuditEventType.ExecutionFailed == 8,
            "13G execution audit event numeric bindings match application contracts");

        var constraints = outbox.GetCheckConstraints()
            .ToDictionary(
                x => x.Name!,
                x => x.Sql!);

        check(
            constraints["CK_SecurityAuditOutbox_Ids"]
                .Contains("\"AuditEventId\" <>") &&
            constraints["CK_SecurityAuditOutbox_Ids"]
                .Contains("\"ExecutionId\" <>"),
            "13G required audit and execution identities cannot be empty");

        check(
            constraints["CK_SecurityAuditOutbox_ToolId"]
                .Contains("^[a-z][a-z0-9-]{0,63}$"),
            "13G audit outbox tool format remains canonical");

        check(
            constraints["CK_SecurityAuditOutbox_Delivery"]
                .Contains("\"DeliveryState\" = 0") &&
            constraints["CK_SecurityAuditOutbox_Delivery"]
                .Contains("\"AttemptCount\" = 0") &&
            constraints["CK_SecurityAuditOutbox_Delivery"]
                .Contains("\"DeliveredAt\" IS NULL"),
            "13G pending outbox state requires zero attempts and no delivery timestamp");

        check(
            outbox.GetForeignKeys().All(fk =>
                fk.DeleteBehavior == DeleteBehavior.Restrict),
            "13G audit outbox provenance foreign keys restrict deletes");

        var migration =
            new AddSecurityAuditOutbox();

        check(
            migration.UpOperations.Count(
                x => x is CreateTableOperation) == 1,
            "13G migration creates exactly one outbox table");

        check(
            migration.UpOperations.All(
                x => x is CreateTableOperation or CreateIndexOperation),
            "13G migration does not mutate existing tables or seed data");

        check(
            migration.DownOperations.Count == 1 &&
            migration.DownOperations.Single()
                is DropTableOperation drop &&
            drop.Name == "SecurityAuditOutbox",
            "13G rollback removes only the security audit outbox table");

        var root = Directory.GetCurrentDirectory();

        var store = File.ReadAllText(Path.Combine(
            root,
            "Aura.Infrastructure",
            "Services",
            "ActionExecutionStatePersistenceStore.cs"));

        check(
            store.Contains(
                "AddPendingAuditOutbox(",
                StringComparison.Ordinal) &&
            store.Contains(
                "db.SecurityAuditOutbox.Add",
                StringComparison.Ordinal),
            "13G execution persistence stages bounded pending audit intents");

        check(
            store.Contains(
                "BeginTransactionAsync",
                StringComparison.Ordinal) &&
            store.Contains(
                "tx.CommitAsync",
                StringComparison.Ordinal),
            "13G execution state and pending audit intent share explicit transaction boundaries");

        check(
            store.Contains(
                "AuditEventType.ExecutionReserved",
                StringComparison.Ordinal) &&
            store.Contains(
                "AuditEventType.ExecutionCompleted",
                StringComparison.Ordinal) &&
            store.Contains(
                "AuditEventType.ExecutionFailed",
                StringComparison.Ordinal),
            "13G execution transitions map to exact bounded audit event types");

        check(
            store.Contains(
                "SecurityAuditOutboxDeliveryState.Pending",
                StringComparison.Ordinal) &&
            store.Contains(
                "AttemptCount =",
                StringComparison.Ordinal),
            "13G execution writer creates pending undelivered audit records");

        check(
            !store.Contains(
                "SecurityAuditOutboxDeliveryState.Delivered",
                StringComparison.Ordinal) &&
            !store.Contains(
                "SecurityAuditOutboxDeliveryState.Failed",
                StringComparison.Ordinal),
            "13G execution path cannot claim delivery or failure processing");

        var apiProgram = File.ReadAllText(Path.Combine(
            root,
            "Aura.Api",
            "Program.cs"));

        check(
            !apiProgram.Contains(
                "SecurityAuditOutbox",
                StringComparison.Ordinal),
            "13G no production outbox dispatcher or registration is enabled");
    }
}