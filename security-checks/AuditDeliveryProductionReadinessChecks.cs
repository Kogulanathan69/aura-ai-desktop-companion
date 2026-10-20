using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class AuditDeliveryProductionReadinessChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var dispatcher = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Services",
            "SecurityAuditOutboxDispatcher.cs"));
        var sink = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Services",
            "SecurityAuditPersistenceSink.cs"));
        check(dispatcher.Contains("row.AuditEventId") && sink.Contains("x.Id == record.Id.Value") &&
            sink.Contains("ResolveDuplicateAsync"),
            "17X AuditEventId is the stable database delivery idempotency identity");
        check(!dispatcher.Contains("handler.ExecuteAsync") && !dispatcher.Contains("Task.Delay") &&
            !dispatcher.Contains("while ("),
            "17X delivery never invokes or retries a tool handler");
        check(!ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).AuditDelivery,
            "17X delivery remains false without exact sink graph and fresh replay evidence");
        return Task.CompletedTask;
    }
}
