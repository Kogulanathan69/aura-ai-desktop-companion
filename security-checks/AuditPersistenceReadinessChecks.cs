using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Security;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

internal static class AuditPersistenceReadinessChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector())
            .Options;
        await using var db = new AuraDbContext(options);
        var audit = db.Model.FindEntityType(typeof(AuditLog))!;
        var fields = audit.GetProperties().Select(property => property.Name).ToHashSet();
        check(audit.GetTableName() == "AuditLogs" &&
            fields.Contains(nameof(AuditLog.Id)) &&
            audit.FindPrimaryKey()!.Properties.Single().Name == nameof(AuditLog.Id) &&
            fields.Contains(nameof(AuditLog.EventType)) &&
            fields.Contains(nameof(AuditLog.Result)) &&
            fields.Contains(nameof(AuditLog.CreatedAt)),
            "12I generic AuditLog has a unique row ID, event/result text and timestamp");
        check(audit.FindProperty(nameof(AuditLog.UserId))!.IsNullable &&
            audit.FindProperty(nameof(AuditLog.ProjectId))!.IsNullable &&
            !fields.Contains("ConversationId"),
            "12I exact required user/conversation/project audit scope is absent");
        foreach (var missing in new[] { "ActionId", "ToolId", "ApprovalId", "ExecutionId",
            "VerificationId", "ReconciliationId", "PermissionGrantId" })
            check(!fields.Contains(missing),
                "12I missing typed audit reference " + missing);
        check(fields.Contains(nameof(AuditLog.EntityType)) &&
            fields.Contains(nameof(AuditLog.EntityId)) &&
            fields.Contains(nameof(AuditLog.Description)) &&
            audit.FindProperty(nameof(AuditLog.OldValue))!.GetColumnType() == "jsonb" &&
            audit.FindProperty(nameof(AuditLog.NewValue))!.GetColumnType() == "jsonb" &&
            audit.FindProperty(nameof(AuditLog.Description))!.GetColumnType() == "text",
            "12I generic entity/text/JSON fields cannot carry bounded typed references");
        check(!db.Model.GetEntityTypes().Any(entity =>
                entity.GetTableName()?.Contains("Outbox", StringComparison.OrdinalIgnoreCase) == true) &&
            !audit.GetProperties().Any(property => property.Name is "DeliveredAt" or
                "PublishedAt" or "DeliveryState" or "DeliveryAttemptCount"),
            "12I no transactional audit outbox or delivery state exists in EF model");
        check(!audit.GetIndexes().Any(index => index.Properties.Any(property =>
                property.Name is "ActionId" or "ExecutionId" or "Sequence")) &&
            !fields.Contains("Sequence") && !fields.Contains("Version"),
            "12I timestamp alone supplies no per-action or per-execution sequence");
        check(!audit.GetProperties().Any(property => property.IsConcurrencyToken) &&
            !fields.Contains("PreviousHash") && !fields.Contains("Signature"),
            "12I model has no tamper-evident chain or immutable-write policy");

        var sinks = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IAuditEventSink).Assembly.GetTypes())
            .Concat(typeof(RuntimeSafetyOptions).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract &&
                typeof(IAuditEventSink).IsAssignableFrom(type)).ToArray();
        check(sinks.SequenceEqual([typeof(UnavailableAuditEventSink)]),
            "12I incompatible schema has no production audit sink adapter");
        check(await new UnavailableAuditEventSink().WriteAsync(null!, CancellationToken.None) ==
            AuditSinkStatus.Unavailable,
            "12I default audit sink remains unavailable without a database write");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var propagated = false;
            try { await new UnavailableAuditEventSink().WriteAsync(null!, cancelled.Token); }
            catch (OperationCanceledException) { propagated = true; }
            check(propagated, "12I unavailable audit sink propagates cancellation");
        }
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var defaults = new RuntimeSafetyOptions();
        check(!startup.AuditSink && !informational.AuditSink &&
            !defaults.AuditPersistenceEnabled && !defaults.ProductionAdaptersEnabled &&
            !defaults.ToolExecutionEnabled && !defaults.SafeFileAccessEnabled,
            "12I startup and informational audit readiness remain disabled");
    }
}
