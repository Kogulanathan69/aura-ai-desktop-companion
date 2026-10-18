using Aura.Api.Security;
using Aura.Application.Tools;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionFirstToolHandlerChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var contracts = File.ReadAllText(Path.Combine(root, "Aura.Application", "Tools",
            "ToolContracts.cs"));
        var trusted = File.ReadAllText(Path.Combine(root, "Aura.Application", "Executions",
            "TrustedToolExecutionService.cs"));
        var inspector = File.ReadAllText(Path.Combine(root, "Aura.Api", "Security",
            "ProductionRuntimeCompositionInspector.cs"));
        check(typeof(ToolInvocationRequest).GetProperties().Select(x => x.Name)
                .SequenceEqual(["ToolId"]) &&
            contracts.Contains("record ToolInvocationRequest(ToolIdentifier ToolId)") &&
            trusted.Contains("new ToolInvocationRequest(action.ToolId)") &&
            !api.Contains("AddScoped<IToolHandler") &&
            inspector.Contains("Array.Empty<(Type, string, ToolCapability, ToolPermissionRequirement)>()") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).ToolHandlers,
            "17H no trusted resource target, production handler or reviewed registry entry");
        return Task.CompletedTask;
    }
}
