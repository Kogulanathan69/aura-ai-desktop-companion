using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Security;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionAuditDeliveryCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).AuditDelivery;

        check(!Ready(new ServiceCollection()), "14J empty registrations cannot assert audit delivery");
        var unavailable = new ServiceCollection();
        unavailable.AddScoped<IAuditOutboxDispatcher, UnavailableDispatcher>();
        check(!Ready(unavailable), "14J unavailable dispatcher is not production-ready");

        var exact = new ServiceCollection();
        exact.AddScoped<IAuditOutboxDispatcher, SecurityAuditOutboxDispatcher>();
        check(!Ready(exact), "14J exact scoped dispatcher without reviewed catalog remains false");
        exact.AddScoped<IAuditOutboxDispatcher, SecurityAuditOutboxDispatcher>();
        check(!Ready(exact), "14J duplicate dispatcher registrations remain false");

        var factory = new ServiceCollection();
        factory.AddScoped<IAuditOutboxDispatcher>(_ => throw new InvalidOperationException());
        check(!Ready(factory), "14J dispatcher factory registration remains false");
        var instance = new ServiceCollection();
        instance.AddSingleton<IAuditOutboxDispatcher>(new UnavailableDispatcher());
        check(!Ready(instance), "14J dispatcher instance registration remains false");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<IAuditOutboxDispatcher, SecurityAuditOutboxDispatcher>();
        check(!Ready(singleton), "14J singleton dispatcher remains false");
        var transient = new ServiceCollection();
        transient.AddTransient<IAuditOutboxDispatcher, SecurityAuditOutboxDispatcher>();
        check(!Ready(transient), "14J transient dispatcher remains false");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedAuditDelivery",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 1 } && catalog.Contains(typeof(SecurityAuditOutboxDispatcher)) &&
            !Ready(new ServiceCollection()),
            "17X exact delivery catalog cannot replace DI and fresh changed-path evidence");

        var sinkAndLogs = new ServiceCollection();
        sinkAndLogs.AddScoped<IAuditEventSink, SecurityAuditPersistenceSink>();
        sinkAndLogs.AddLogging();
        check(!Ready(sinkAndLogs),
            "14J audit sink and ILogger cannot substitute for outbox delivery");

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var dispatcher = Read("Aura.Infrastructure", "Services", "SecurityAuditOutboxDispatcher.cs");
        var outbox = Read("Aura.Domain", "Entities", "SecurityAuditOutboxRecord.cs");
        var configuration = Read("Aura.Infrastructure", "Data", "Configurations",
            "SecurityAuditOutboxRecordConfiguration.cs");
        var migration = Read("Aura.Infrastructure", "Data", "Migrations",
            "20261014000004_HardenSecurityAuditOutboxDelivery.cs");
        var api = Read("Aura.Api", "Program.cs");

        check(inspector.Contains("ImplementationType == typeof(SecurityAuditOutboxDispatcher)",
                StringComparison.Ordinal) &&
            inspector.Contains("ReviewedAuditDelivery.Contains(typeof(SecurityAuditOutboxDispatcher))",
                StringComparison.Ordinal) &&
            inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Scoped", StringComparison.Ordinal),
            "14J delivery requires one scoped exact implementation and separate review");
        check(outbox.Contains("AuditEventId", StringComparison.Ordinal) &&
            outbox.Contains("ActionId", StringComparison.Ordinal) &&
            outbox.Contains("UserId", StringComparison.Ordinal) &&
            outbox.Contains("ConversationId", StringComparison.Ordinal) &&
            outbox.Contains("ProjectId", StringComparison.Ordinal) &&
            outbox.Contains("ToolId", StringComparison.Ordinal) &&
            outbox.Contains("AttemptCount", StringComparison.Ordinal) &&
            outbox.Contains("DeliveredAt", StringComparison.Ordinal) &&
            configuration.Contains("CK_SecurityAuditOutbox_Delivery", StringComparison.Ordinal) &&
            migration.Contains("CK_SecurityAuditOutbox_Delivery", StringComparison.Ordinal) &&
            !Ready(new ServiceCollection()),
            "14J durable outbox model, state constraint and migration cannot assert delivery");
        check(dispatcher.Contains("db.SecurityAuditOutbox", StringComparison.Ordinal) &&
            dispatcher.Contains("AuditEventRecord.RestoreExecutionOutbox", StringComparison.Ordinal) &&
            dispatcher.Contains("state.ActionId == row.ActionId", StringComparison.Ordinal) &&
            dispatcher.Contains("state.UserId == row.UserId", StringComparison.Ordinal) &&
            dispatcher.Contains("state.ConversationId ==", StringComparison.Ordinal) &&
            dispatcher.Contains("state.ProjectId ==", StringComparison.Ordinal) &&
            dispatcher.Contains("state.ToolId ==", StringComparison.Ordinal) &&
            dispatcher.Contains("state.ExecutionId ==", StringComparison.Ordinal) &&
            dispatcher.Contains("x.AuditEventId == row.AuditEventId", StringComparison.Ordinal),
            "14J explicit dispatcher preserves stable event and exact execution-state binding");
        check(dispatcher.Contains("SecurityAuditOutboxDeliveryState.Pending", StringComparison.Ordinal) &&
            dispatcher.Contains("SecurityAuditOutboxDeliveryState.Delivered", StringComparison.Ordinal) &&
            dispatcher.Contains("SecurityAuditOutboxDeliveryState.Failed", StringComparison.Ordinal) &&
            dispatcher.Contains("x.AttemptCount == 0", StringComparison.Ordinal) &&
            dispatcher.Contains("x.DeliveredAt == null", StringComparison.Ordinal) &&
            dispatcher.Contains("x => x.AttemptCount,", StringComparison.Ordinal) &&
            dispatcher.Contains("x => x.DeliveredAt,", StringComparison.Ordinal) &&
            dispatcher.Contains("AuditOutboxDispatchStatus.Unavailable", StringComparison.Ordinal) &&
            dispatcher.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
            "14J terminal state, one attempt, timestamp and unavailable/cancellation remain explicit");
        check(!dispatcher.Contains("while (", StringComparison.Ordinal) &&
            !dispatcher.Contains("Task.Delay", StringComparison.Ordinal) &&
            !dispatcher.Contains("db.AuditLogs", StringComparison.Ordinal) &&
            !dispatcher.Contains("MigrateAsync", StringComparison.Ordinal),
            "14J dispatcher has no hidden retry, generic log fallback or migration");

        var partial = new ProductionRuntimeCompositionSnapshot(true, true, true, true, false,
            true, true, true, true, true);
        check(!partial.AuditDelivery && !partial.CompositionComplete && !Ready(sinkAndLogs),
            "14J AuditSink and all other capabilities cannot infer AuditDelivery");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.AuditPersistenceEnabled && !Ready(new ServiceCollection()) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().AuditSink,
            "14J flags and disposable live evidence cannot replace production delivery DI");
        check(api.Contains("AddStateAuditReconciliationRuntimeFoundation", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "17X normal API has exact dispatcher registration without runtime readiness");
        check(!api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("BackgroundService", StringComparison.Ordinal) &&
            !api.Contains("PeriodicTimer", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !inspector.Contains("GetRequiredService", StringComparison.Ordinal) &&
            !inspector.Contains("BuildServiceProvider", StringComparison.Ordinal) &&
            !inspector.Contains("ExecuteAsync", StringComparison.Ordinal) &&
            !inspector.Contains("HttpClient", StringComparison.Ordinal) &&
            !inspector.Contains("File.", StringComparison.Ordinal),
            "14J introduces no worker, scheduler, migration, live DB, provider, file or tool call");
        var defaults = new RuntimeSafetyOptions();
        check(!Ready(new ServiceCollection()) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled,
            "14J production delivery and execution remain disabled");
        return Task.CompletedTask;
    }

    private sealed class UnavailableDispatcher : IAuditOutboxDispatcher
    {
        public Task<AuditOutboxDispatchResult> DispatchAsync(Guid outboxId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AuditOutboxDispatchResult(AuditOutboxDispatchStatus.Unavailable));
        }
    }
}
