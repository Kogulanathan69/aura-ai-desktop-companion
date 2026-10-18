using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionAuditDeliveryRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Read("Aura.Api", "Program.cs");
        var dispatcher = Read("Aura.Infrastructure", "Services", "SecurityAuditOutboxDispatcher.cs");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(typeof(IAuditOutboxDispatcher).IsAssignableFrom(typeof(SecurityAuditOutboxDispatcher)) &&
            dispatcher.Contains("IDbContextFactory<AuraDbContext>") &&
            dispatcher.Contains("IAuditEventSink") &&
            !dispatcher.Contains("BackgroundService") &&
            !dispatcher.Contains("PeriodicTimer"),
            "17E exact dispatcher is explicit one-item work with no polling worker");
        check(!api.Contains("AddScoped<IAuditOutboxDispatcher") &&
            inspector.Contains("ReviewedAuditDelivery = new HashSet<Type>()") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).AuditDelivery &&
            !api.Contains("AddHostedService"),
            "17E unresolved replay contract keeps delivery unregistered and unready");
        return Task.CompletedTask;
    }
}
