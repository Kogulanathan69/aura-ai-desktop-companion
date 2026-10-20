using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class AuditSinkProductionReadinessChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var sink = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Services",
            "SecurityAuditPersistenceSink.cs"));
        var fields = typeof(AuditEventRecord).GetProperties().Select(x => x.Name).ToArray();
        check(!fields.Any(x => x.Contains("Password") || x.Contains("Connection") ||
                x.Contains("Token") || x.Contains("ApiKey") || x.Contains("Payload") ||
                x.Contains("Exception") || x.Contains("Message")) &&
            sink.Contains("ToolId = record.ToolId?.Value"),
            "17X audit event contract excludes secret, payload and exception text fields");
        check(!sink.Contains("Update(") && !sink.Contains("Remove(") &&
            sink.Contains("ResolveDuplicateAsync"),
            "17X application sink is append-only with exact idempotent duplicate matching");
        check(!ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).AuditSink,
            "17X audit sink remains false without exact graph and reviewed live evidence");
        return Task.CompletedTask;
    }
}
