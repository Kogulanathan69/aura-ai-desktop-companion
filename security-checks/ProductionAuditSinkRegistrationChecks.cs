using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionAuditSinkRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Read("Aura.Api", "Program.cs");
        var sink = Read("Aura.Infrastructure", "Services", "SecurityAuditPersistenceSink.cs");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(typeof(IAuditEventSink).IsAssignableFrom(typeof(SecurityAuditPersistenceSink)) &&
            sink.Contains("IDbContextFactory<AuraDbContext>") &&
            sink.Contains("CreateDbContextAsync") &&
            !sink.Contains("MigrateAsync") && !sink.Contains("EnsureCreated"),
            "17D exact direct sink uses operation context without schema mutation");
        check(api.Contains("AddStateAuditReconciliationRuntimeFoundation") &&
            inspector.Contains("ReviewedAuditSinkLiveEvidence") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).AuditSink &&
            !api.Contains("AddHostedService"),
            "17X exact sink graph remains gated and does not imply delivery");
        return Task.CompletedTask;
    }
}
