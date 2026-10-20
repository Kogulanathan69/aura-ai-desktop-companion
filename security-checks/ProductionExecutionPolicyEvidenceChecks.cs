using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionExecutionPolicyEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var method = typeof(IToolExecutionPolicy).GetMethod(nameof(IToolExecutionPolicy.AllowsAsync));
        check(method is not null && method.ReturnType == typeof(Task<bool>) &&
            method.GetParameters().Select(p => p.ParameterType)
                .SequenceEqual([typeof(ToolDescriptor), typeof(CancellationToken)]),
            "15H policy contract has only exact descriptor and cancellation input");
        check(typeof(IToolExecutionPolicy).GetMethods().Length == 1 &&
            typeof(ToolDescriptor).GetProperties().Select(p => p.Name).SequenceEqual(
                ["Id", "DisplayName", "Description", "Capability", "RequiredPermission", "RequiresApproval", "RequiresOwnedScope", "Enabled"]) &&
            typeof(ToolDescriptor).GetProperties().All(p => p.SetMethod is null) &&
            typeof(ToolExecutionResult).GetProperties().Select(p => p.Name).SequenceEqual(["Status", "Message"]),
            "15H policy has no arbitrary command, path, URL, payload or secret-bearing result");

        ToolIdentifier.TryCreate("fixture", out var id);
        var deny = new DenyToolExecutionPolicy();
        foreach (var capability in Enum.GetValues<ToolCapability>())
        foreach (var permission in Enum.GetValues<ToolPermissionRequirement>())
        {
            var descriptor = new ToolDescriptor(id!, "Fixture", "Synthetic fixture", capability, permission);
            check(!await deny.AllowsAsync(descriptor, CancellationToken.None),
                "15H deny policy rejects valid descriptor and metadata pair " + capability + "/" + permission);
        }
        check(!await deny.AllowsAsync(null!, CancellationToken.None),
            "15H malformed null policy request denies");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var propagated = false;
            try { await deny.AllowsAsync(new ToolDescriptor(id!, "Fixture", "Synthetic fixture",
                ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead), cancelled.Token); }
            catch (OperationCanceledException) { propagated = true; }
            check(propagated, "15H deny policy propagates cancellation");
        }
        check(!ToolIdentifier.TryCreate("/unknown", out _) &&
            !ToolIdentifier.TryCreate("https://unknown", out _) &&
            !Enum.IsDefined((ToolCapability)999) && !Enum.IsDefined((ToolPermissionRequirement)999),
            "15H unknown tool identifier and metadata are not valid descriptors");

        var productionPolicies = typeof(IToolExecutionPolicy).Assembly.GetTypes()
            .Concat(typeof(ProductionRuntimeCompositionInspector).Assembly.GetTypes())
            .Concat(typeof(AuraDbContext).Assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IToolExecutionPolicy).IsAssignableFrom(t))
            .ToArray();
        check(productionPolicies.SequenceEqual([typeof(DenyToolExecutionPolicy)]),
            "15H production assemblies contain only deny policy");
        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedExecutionPolicy",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 0 }, "15H reviewed production policy catalog is empty");

        var empty = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        var denyServices = new ServiceCollection();
        denyServices.AddScoped<IToolExecutionPolicy, DenyToolExecutionPolicy>();
        var denied = ProductionRuntimeCompositionInspector.Inspect(denyServices);
        var synthetic = new ServiceCollection();
        synthetic.AddScoped<IToolExecutionPolicy, SyntheticAllowPolicy>();
        var unreviewed = ProductionRuntimeCompositionInspector.Inspect(synthetic);
        check(!empty.ExecutionPolicy && !denied.ExecutionPolicy && !unreviewed.ExecutionPolicy &&
            !empty.CompositionComplete && !denied.CompositionComplete && !unreviewed.CompositionComplete,
            "15H absent, deny and unreviewed allow registrations remain NOT READY");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.ToolExecutionEnabled &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().ExecutionPolicy &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).ExecutionPolicy,
            "15H unavailable runtime readiness cannot be changed by option flags");

        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var execution = Source("Aura.Application", "Executions", "TrustedToolExecutionService.cs");
        var contracts = Source("Aura.Application", "Executions", "ExecutionContracts.cs");
        var registry = Source("Aura.Application", "Tools", "ToolRegistry.cs");
        var entry = Source("security-checks", "Program.cs");
        check(entry.Contains("await ProductionExecutionPolicyEvidenceChecks.RunAsync(Check)") &&
            !api.Contains("AddScoped<IToolExecutionPolicy") &&
            !api.Contains("AddSingleton<IToolExecutionPolicy") &&
            !api.Contains("AddTransient<IToolExecutionPolicy") &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()"),
            "15H normal API startup does not register policy or assert readiness");
        check(inspector.Contains("ReviewedExecutionPolicy = new HashSet<Type>()") &&
            inspector.Contains("registrations.Length == 1") &&
            inspector.Contains("ServiceLifetime.Scoped") &&
            inspector.Contains("implementation != typeof(DenyToolExecutionPolicy)") &&
            inspector.Contains("ReviewedExecutionPolicy.Contains(implementation)"),
            "15H future policy needs unique exact reviewed scoped type, no factory or instance ambiguity");
        check(execution.Contains("ApprovalValidation.IsBoundToApprovedAction") &&
            execution.Contains("scopes.ValidateActionAsync(exactAction, cancellationToken)") &&
            execution.Contains("permissions.HasPermissionAsync(scope, tool, cancellationToken)") &&
            execution.Contains("policy.AllowsAsync(tool, cancellationToken)") &&
            execution.IndexOf("policy.AllowsAsync", StringComparison.Ordinal) <
                execution.IndexOf("handler.ExecuteAsync", StringComparison.Ordinal) &&
            execution.Contains("if (!allowed) return ExecutionOperationResult.Denied(ExecutionOperationStatus.PolicyDenied)"),
            "15H approval, ownership, permission and handler presence do not bypass policy denial");
        check(execution.Contains("if (!tools.TryResolve(action.ToolId", StringComparison.Ordinal) &&
            execution.Contains("ExecutionOperationStatus.UnknownTool") &&
            execution.Contains("ExecutionOperationStatus.InvalidActionState") &&
            execution.Contains("catch (OperationCanceledException) { throw; }") &&
            execution.Contains("catch (Exception) { return ExecutionOperationResult.Denied") &&
            execution.Contains("cancellationToken.ThrowIfCancellationRequested();") &&
            !execution.Contains("new DenyToolExecutionPolicy") &&
            !execution.Contains("Task.Delay") && !execution.Contains("while (") &&
            registry.Contains("ToolExecutionStatus.Disabled"),
            "15H unknown inputs and policy exceptions fail closed, cancellation propagates, no policy retry or fallback");
        check(!contracts.Contains("HttpClient") && !contracts.Contains("File.") &&
            !contracts.Contains("Process.") && !contracts.Contains("Socket") &&
            !execution.Contains("HttpClient") && !execution.Contains("Process.") &&
            !execution.Contains("File.") && !api.Contains("AddHostedService"),
            "15H current policy boundary adds no cloud, file, process, network or worker dependency");
    }

    private sealed class SyntheticAllowPolicy : IToolExecutionPolicy
    {
        public Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
