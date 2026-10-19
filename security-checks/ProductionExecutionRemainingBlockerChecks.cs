using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Application.Verifications;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionExecutionRemainingBlockerChecks
{
    private static readonly string Root = Directory.GetCurrentDirectory();
    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    internal static Task VerificationAsync(Action<bool, string> check)
    {
        var contracts = Source("Aura.Application", "Verifications", "VerificationContracts.cs");
        var workflow = Source("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        var api = Source("Aura.Api", "Program.cs");
        var production = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IExecutionEvidenceSource).Assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IExecutionEvidenceSource).IsAssignableFrom(t))
            .ToArray();
        check(production.ToHashSet().SetEquals([typeof(UnavailableExecutionEvidenceSource),
                typeof(Aura.Infrastructure.Services.OwnedProjectMetadataExecutionEvidenceSource)]) &&
            contracts.Contains("Proof", StringComparison.OrdinalIgnoreCase) &&
            !contracts.Contains("ProofVersion { get; }") &&
            workflow.Contains("executions.GetAsync(scope, action.Id, request.ExecutionId, cancellationToken)") &&
            !workflow.Contains("SaveChangesAsync") &&
            !api.Contains("AddScoped<IExecutionEvidenceSource") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Verification,
            "16G no independent proof adapter/version binding or production verification registration exists");
        return Task.CompletedTask;
    }

    internal static Task PolicyAsync(Action<bool, string> check)
    {
        var api = Source("Aura.Api", "Program.cs");
        var policies = typeof(IToolExecutionPolicy).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IToolExecutionPolicy).IsAssignableFrom(t))
            .ToArray();
        check(policies.SequenceEqual([typeof(DenyToolExecutionPolicy)]) &&
            typeof(IToolExecutionPolicy).GetMethod("AllowsAsync")!.GetParameters()
                .Select(p => p.ParameterType).SequenceEqual([typeof(ToolDescriptor), typeof(CancellationToken)]) &&
            !api.Contains("AddScoped<IToolExecutionPolicy") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).ExecutionPolicy,
            "16H no reviewed exact tool or scoped allowing policy; deny-only remains production truth");
        return Task.CompletedTask;
    }

    internal static Task HandlersAsync(Action<bool, string> check)
    {
        var api = Source("Aura.Api", "Program.cs");
        var handlers = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IToolHandler).Assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IToolHandler).IsAssignableFrom(t))
            .ToArray();
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        check(handlers.Length == 0 && !api.Contains("AddScoped<IToolHandler") &&
            !api.Contains("ToolRegistry(") && !snapshot.ToolHandlers &&
            !snapshot.TrustedExecution && !snapshot.CompositionComplete,
            "16I no safe scope-bound production handler or reviewed exact registry set exists");
        return Task.CompletedTask;
    }
}
