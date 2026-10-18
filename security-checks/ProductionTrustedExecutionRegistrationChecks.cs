using Aura.Api.Security;
using Aura.Application.Executions;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionTrustedExecutionRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var inspector = File.ReadAllText(Path.Combine(root, "Aura.Api", "Security",
            "ProductionRuntimeCompositionInspector.cs"));
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        check(typeof(ITrustedToolExecutionService).IsAssignableFrom(
                typeof(TrustedToolExecutionService)) &&
            !api.Contains("AddScoped<ITrustedToolExecutionService") &&
            inspector.Contains("ReviewedTrustedExecution = new HashSet<Type>()") &&
            !snapshot.TrustedExecution && !snapshot.CompositionComplete,
            "17J missing prerequisites prevent trusted production registration");
        return Task.CompletedTask;
    }
}
