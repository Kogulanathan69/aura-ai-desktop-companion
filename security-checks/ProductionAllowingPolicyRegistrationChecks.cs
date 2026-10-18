using Aura.Api.Security;
using Aura.Application.Executions;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionAllowingPolicyRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var inspector = File.ReadAllText(Path.Combine(root, "Aura.Api", "Security",
            "ProductionRuntimeCompositionInspector.cs"));
        check(typeof(IToolExecutionPolicy).IsAssignableFrom(typeof(DenyToolExecutionPolicy)) &&
            !api.Contains("AddScoped<IToolExecutionPolicy") &&
            inspector.Contains("ReviewedExecutionPolicy = new HashSet<Type>()") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).ExecutionPolicy,
            "17I absent reviewed tool keeps production policy deny only");
        return Task.CompletedTask;
    }
}
