using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionExecutionPolicyCompositionChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).ExecutionPolicy;

        check(!Ready(new ServiceCollection()), "14M empty registrations cannot assert execution policy");
        var deny = new ServiceCollection();
        deny.AddScoped<IToolExecutionPolicy, DenyToolExecutionPolicy>();
        check(!Ready(deny), "14M scoped deny-only policy is never production-ready");
        var defaultDeny = new ServiceCollection();
        defaultDeny.AddScoped<IToolExecutionPolicy, UnavailablePolicy>();
        check(!Ready(defaultDeny), "14M default or unavailable deny policy cannot count");

        var unreviewed = new ServiceCollection();
        unreviewed.AddScoped<IToolExecutionPolicy, SyntheticAllowPolicy>();
        check(!Ready(unreviewed), "14M scoped synthetic allowing policy without review remains false");
        unreviewed.AddScoped<IToolExecutionPolicy, SyntheticAllowPolicy>();
        check(!Ready(unreviewed), "14M duplicate policy registrations remain false");

        var factory = new ServiceCollection();
        factory.AddScoped<IToolExecutionPolicy>(_ => throw new InvalidOperationException());
        check(!Ready(factory), "14M policy factory registration remains false");
        var instance = new ServiceCollection();
        instance.AddSingleton<IToolExecutionPolicy>(new DenyToolExecutionPolicy());
        check(!Ready(instance), "14M policy instance registration remains false");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<IToolExecutionPolicy, SyntheticAllowPolicy>();
        check(!Ready(singleton), "14M singleton policy remains false");
        var transient = new ServiceCollection();
        transient.AddTransient<IToolExecutionPolicy, SyntheticAllowPolicy>();
        check(!Ready(transient), "14M transient policy remains false");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedExecutionPolicy",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IReadOnlySet<Type>;
        check(catalog is { Count: 0 } && !Ready(new ServiceCollection()),
            "14M reviewed policy catalog is empty and cannot replace matching DI");

        var productionPolicies = typeof(DenyToolExecutionPolicy).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract &&
                typeof(IToolExecutionPolicy).IsAssignableFrom(type)).ToArray();
        check(productionPolicies.SequenceEqual([typeof(DenyToolExecutionPolicy)]) && !Ready(deny),
            "14M production assembly contains only deny policy, no reviewed allowing policy");

        var otherCapabilities = new ServiceCollection();
        otherCapabilities.AddScoped<IToolExecutionScopeValidator, ExactOwnershipScopeValidator>();
        otherCapabilities.AddScoped<IToolPermissionSource, UnavailableToolPermissionSource>();
        otherCapabilities.AddScoped<IToolHandler, SyntheticHandler>();
        otherCapabilities.AddScoped<ITrustedToolExecutionService, TrustedToolExecutionService>();
        check(!Ready(otherCapabilities),
            "14M ownership, permission, handlers and trusted service cannot substitute for policy");

        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var contracts = Read("Aura.Application", "Executions", "ExecutionContracts.cs");
        var execution = Read("Aura.Application", "Executions", "TrustedToolExecutionService.cs");
        var registry = Read("Aura.Application", "Tools", "ToolRegistry.cs");
        var api = Read("Aura.Api", "Program.cs");

        check(inspector.Contains("HasExactExecutionPolicy(services)", StringComparison.Ordinal) &&
            inspector.Contains("registrations.Length == 1", StringComparison.Ordinal) &&
            inspector.Contains("ServiceLifetime.Scoped", StringComparison.Ordinal) &&
            inspector.Contains("implementation != typeof(DenyToolExecutionPolicy)",
                StringComparison.Ordinal) &&
            inspector.Contains("ReviewedExecutionPolicy.Contains(implementation)", StringComparison.Ordinal),
            "14M one scoped exact catalog-reviewed non-deny policy is required");
        check(contracts.Contains("class DenyToolExecutionPolicy", StringComparison.Ordinal) &&
            contracts.Contains("cancellationToken.ThrowIfCancellationRequested()", StringComparison.Ordinal) &&
            contracts.Contains("return Task.FromResult(false)", StringComparison.Ordinal) &&
            !contracts.Contains("class DefaultDenyExecutionPolicy", StringComparison.Ordinal) &&
            !Ready(deny) && !Ready(defaultDeny),
            "14M deny and unavailable defaults remain fail-closed with cancellation");
        check(execution.Contains("ApprovalValidation.IsBoundToApprovedAction", StringComparison.Ordinal) &&
            execution.Contains("scopes.ValidateActionAsync(exactAction, cancellationToken)", StringComparison.Ordinal) &&
            execution.Contains("permissions.HasPermissionAsync(scope, tool, cancellationToken)",
                StringComparison.Ordinal) &&
            execution.Contains("policy.AllowsAsync(tool, cancellationToken)", StringComparison.Ordinal) &&
            execution.Contains("if (!allowed) return ExecutionOperationResult.Denied(ExecutionOperationStatus.PolicyDenied)",
                StringComparison.Ordinal) &&
            execution.IndexOf("policy.AllowsAsync", StringComparison.Ordinal) <
                execution.IndexOf("handler.ExecuteAsync", StringComparison.Ordinal),
            "14M approval, ownership and permission are necessary but cannot bypass independent policy");
        check(execution.Contains("ExecutionOperationStatus.InvalidActionState", StringComparison.Ordinal) &&
            execution.Contains("ExecutionOperationStatus.UnknownTool", StringComparison.Ordinal) &&
            execution.Contains("catch (OperationCanceledException) { throw; }", StringComparison.Ordinal) &&
            registry.Contains("ToolExecutionStatus.Disabled", StringComparison.Ordinal) &&
            !execution.Contains("new DenyToolExecutionPolicy", StringComparison.Ordinal) &&
            !execution.Contains("new SyntheticAllowPolicy", StringComparison.Ordinal),
            "14M unknown action/tool states deny and execution has no policy fallback");

        var partial = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            true, true, false, true, true);
        check(!partial.ExecutionPolicy && !partial.CompositionComplete && !Ready(otherCapabilities),
            "14M all other capabilities cannot infer execution policy or complete composition");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.ToolExecutionEnabled && !Ready(new ServiceCollection()) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().ExecutionPolicy,
            "14M runtime flags cannot substitute for reviewed policy DI");
        check(!api.Contains("AddScoped<IToolExecutionPolicy", StringComparison.Ordinal) &&
            !api.Contains("AddSingleton<IToolExecutionPolicy", StringComparison.Ordinal) &&
            !api.Contains("AddTransient<IToolExecutionPolicy", StringComparison.Ordinal) &&
            !api.Contains("DenyToolExecutionPolicy", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "14M normal API has no policy registration or readiness claim");
        check(!api.Contains("AddHostedService", StringComparison.Ordinal) &&
            !api.Contains("BackgroundService", StringComparison.Ordinal) &&
            !api.Contains("PeriodicTimer", StringComparison.Ordinal) &&
            !api.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal) &&
            !inspector.Contains("GetRequiredService", StringComparison.Ordinal) &&
            !inspector.Contains("BuildServiceProvider", StringComparison.Ordinal) &&
            !inspector.Contains("ExecuteAsync", StringComparison.Ordinal) &&
            !inspector.Contains("HttpClient", StringComparison.Ordinal) &&
            !inspector.Contains("File.", StringComparison.Ordinal),
            "14M introduces no execution, worker, scheduler, migration, DB, provider or file call");
        var defaults = new RuntimeSafetyOptions();
        check(!Ready(new ServiceCollection()) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled,
            "14M production execution and optional capabilities remain disabled");
        return Task.CompletedTask;
    }

    private sealed class UnavailablePolicy : IToolExecutionPolicy
    {
        public Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private sealed class SyntheticAllowPolicy : IToolExecutionPolicy
    {
        public Task<bool> AllowsAsync(ToolDescriptor tool, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }

    private sealed class SyntheticHandler : IToolHandler
    {
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ToolExecutionResult.FromStatus(ToolExecutionStatus.Disabled));
        }
    }
}
