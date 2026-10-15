using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Security;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionAuditSinkCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).AuditSink;

        check(!Ready(new ServiceCollection()), "14I empty registrations cannot assert audit sink");
        var unavailable = new ServiceCollection();
        unavailable.AddScoped<IAuditEventSink, UnavailableAuditEventSink>();
        check(!Ready(unavailable), "14I unavailable sink is not production-ready");

        var exact = new ServiceCollection();
        exact.AddScoped<IAuditEventSink, SecurityAuditPersistenceSink>();
        check(!Ready(exact), "14I exact scoped sink without reviewed catalog remains false");
        exact.AddScoped<IAuditEventSink, SecurityAuditPersistenceSink>();
        check(!Ready(exact), "14I duplicate sink registrations remain false");

        var factory = new ServiceCollection();
        factory.AddScoped<IAuditEventSink>(_ => throw new InvalidOperationException());
        check(!Ready(factory), "14I sink factory registration remains false");
        var instance = new ServiceCollection();
        instance.AddSingleton<IAuditEventSink>(new UnavailableAuditEventSink());
        check(!Ready(instance), "14I sink instance registration remains false");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<IAuditEventSink, SecurityAuditPersistenceSink>();
        check(!Ready(singleton), "14I singleton sink remains false");
        var transient = new ServiceCollection();
        transient.AddTransient<IAuditEventSink, SecurityAuditPersistenceSink>();
        check(!Ready(transient), "14I transient sink remains false");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedAuditSink",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 0 } && !Ready(new ServiceCollection()),
            "14I catalog remains empty and cannot replace a DI registration");

        var deliveryOnly = new ServiceCollection();
        deliveryOnly.AddScoped<IAuditOutboxDispatcher, SecurityAuditOutboxDispatcher>();
        deliveryOnly.AddLogging();
        check(!Ready(deliveryOnly),
            "14I dispatcher and ILogger registrations cannot substitute for audit persistence");

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var sink = Read("Aura.Infrastructure", "Services", "SecurityAuditPersistenceSink.cs");
        var eventEntity = Read("Aura.Domain", "Entities", "SecurityAuditEventRecord.cs");
        var outbox = Read("Aura.Domain", "Entities", "SecurityAuditOutboxRecord.cs");
        var legacy = Read("Aura.Domain", "Entities", "AuditLog.cs");
        var migration = Read("Aura.Infrastructure", "Data", "Migrations",
            "20261014000002_AddSecurityPersistenceBundle.cs");
        var api = Read("Aura.Api", "Program.cs");

        check(inspector.Contains("ImplementationType == typeof(SecurityAuditPersistenceSink)",
                StringComparison.Ordinal) &&
            inspector.Contains("ReviewedAuditSink.Contains(typeof(SecurityAuditPersistenceSink))",
                StringComparison.Ordinal) &&
            inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Scoped", StringComparison.Ordinal),
            "14I sink requires unique scoped exact implementation and separate review");
        check(sink.Contains("db.SecurityAuditEvents.Add", StringComparison.Ordinal) &&
            sink.Contains("SaveChangesAsync(cancellationToken) == 1", StringComparison.Ordinal) &&
            !sink.Contains("db.AuditLogs", StringComparison.Ordinal) &&
            !sink.Contains("SecurityAuditOutbox.Add", StringComparison.Ordinal) &&
            eventEntity.Contains("ActionId", StringComparison.Ordinal) &&
            eventEntity.Contains("UserId", StringComparison.Ordinal) &&
            eventEntity.Contains("ConversationId", StringComparison.Ordinal) &&
            eventEntity.Contains("ProjectId", StringComparison.Ordinal) &&
            eventEntity.Contains("ToolId", StringComparison.Ordinal) &&
            legacy.Contains("AuditLog", StringComparison.Ordinal) &&
            migration.Contains("SecurityAuditEvents", StringComparison.Ordinal) &&
            outbox.Contains("SecurityAuditOutboxRecord", StringComparison.Ordinal) &&
            !Ready(new ServiceCollection()),
            "14I direct bounded SecurityAuditEvents model, legacy log and outbox existence do not assert readiness");
        check(sink.Contains("existing.UserId == record.Scope.UserId", StringComparison.Ordinal) &&
            sink.Contains("existing.ConversationId == record.Scope.ConversationId", StringComparison.Ordinal) &&
            sink.Contains("existing.ProjectId == record.Scope.ProjectId", StringComparison.Ordinal) &&
            sink.Contains("existing.ActionId == record.ActionId?.Value", StringComparison.Ordinal) &&
            sink.Contains("existing.ToolId == record.ToolId?.Value", StringComparison.Ordinal) &&
            sink.Contains("AuditSinkStatus.Failed", StringComparison.Ordinal) &&
            sink.Contains("AuditSinkStatus.Unavailable", StringComparison.Ordinal) &&
            sink.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
            "14I duplicate acceptance preserves exact bindings and failure/cancellation visibility");
        check(!sink.Contains("Task.Delay", StringComparison.Ordinal) &&
            !sink.Contains("while (", StringComparison.Ordinal) &&
            !sink.Contains("ILogger", StringComparison.Ordinal) &&
            !sink.Contains("MigrateAsync", StringComparison.Ordinal),
            "14I direct sink has no retry, logging fallback or migration path");

        var partial = new ProductionRuntimeCompositionSnapshot(true, true, true, false, true,
            true, true, true, true, true);
        check(!partial.AuditSink && !partial.CompositionComplete && !Ready(deliveryOnly),
            "14I delivery, ownership, permission and execution state cannot infer AuditSink");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.AuditPersistenceEnabled && !Ready(new ServiceCollection()) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().AuditSink,
            "14I runtime flags and existing implementation cannot inflate audit readiness");
        check(!api.Contains("AddScoped<IAuditEventSink", StringComparison.Ordinal) &&
            !api.Contains("AddSingleton<IAuditEventSink", StringComparison.Ordinal) &&
            !api.Contains("AddTransient<IAuditEventSink", StringComparison.Ordinal) &&
            !api.Contains("SecurityAuditPersistenceSink", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "14I normal API has no audit-sink registration or readiness claim");
        check(!api.Contains("AddScoped<IAuditOutboxDispatcher", StringComparison.Ordinal) &&
            !api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !inspector.Contains("GetRequiredService", StringComparison.Ordinal) &&
            !inspector.Contains("BuildServiceProvider", StringComparison.Ordinal) &&
            !inspector.Contains("ExecuteAsync", StringComparison.Ordinal) &&
            !inspector.Contains("HttpClient", StringComparison.Ordinal) &&
            !inspector.Contains("File.", StringComparison.Ordinal),
            "14I introduces no delivery, worker, migration, DB, provider, file or tool call");
        var defaults = new RuntimeSafetyOptions();
        check(!Ready(new ServiceCollection()) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled,
            "14I production execution, audit delivery and optional capabilities remain disabled");
        return Task.CompletedTask;
    }
}
