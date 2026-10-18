using Aura.Api.Security;
using Aura.Application.Security;
using Microsoft.Extensions.DependencyInjection;

internal static class FinalProductionActivationIntegrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var composition = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var approval = new UnavailableFinalProductionSecurityApprovalSource();
        var flags = new RuntimeSafetyOptions();
        var gate = new ProductionActivationGate();
        check(!gate.CanActivate(composition, flags,
                new UnavailableRuntimeCapabilityReadiness().GetSnapshot(),
                RuntimeSafetyStatus.SafeDisabled, approval) &&
            !approval.IsApproved && !flags.ProductionAdaptersEnabled &&
            !flags.ToolExecutionEnabled && !flags.AuditPersistenceEnabled &&
            !flags.SafeFileAccessEnabled && !flags.CloudAiEnabled &&
            api.Contains("new UnavailableFinalProductionSecurityApprovalSource()") &&
            !api.Contains("MapToolExecution") && !api.Contains("AddHostedService"),
            "17J production defaults and unavailable approval deny activation");
        return Task.CompletedTask;
    }
}
