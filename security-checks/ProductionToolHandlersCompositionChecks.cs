using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionToolHandlersCompositionChecks
{
    private static readonly (Type HandlerType, string ToolId, ToolCapability Capability,
        ToolPermissionRequirement Permission) Read =
        (typeof(ReadFixtureHandler), "reviewed-read", ToolCapability.ProjectRead,
            ToolPermissionRequirement.OwnedProjectRead);
    private static readonly (Type HandlerType, string ToolId, ToolCapability Capability,
        ToolPermissionRequirement Permission) Write =
        (typeof(WriteFixtureHandler), "reviewed-write", ToolCapability.ProjectWrite,
            ToolPermissionRequirement.OwnedProjectWrite);

    internal static Task RunAsync(Action<bool, string> check)
    {
        static bool Ready(IServiceCollection services) =>
            ProductionRuntimeCompositionInspector.Inspect(services).ToolHandlers;

        // Invoke the private pure set predicate with synthetic catalogs. Production
        // still passes only its source-controlled empty catalog to this predicate.
        var method = typeof(ProductionRuntimeCompositionInspector).GetMethod(
            "MatchesReviewedToolHandlers", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Matches(IServiceCollection services,
            IReadOnlyList<(Type HandlerType, string ToolId, ToolCapability Capability,
                ToolPermissionRequirement Permission)> catalog) =>
            method.Invoke(null, [services, catalog]) is true;

        check(!Ready(new ServiceCollection()) && !Matches(new ServiceCollection(), []),
            "14N empty services and empty reviewed set cannot assert handler readiness");
        var one = new ServiceCollection();
        one.AddScoped<IToolHandler, ReadFixtureHandler>();
        check(!Ready(one), "14N one scoped unreviewed handler cannot assert complete set");
        check(!Matches(one, [Read, Write]), "14N subset of reviewed handler set remains false");
        check(Matches(one, [Read]) && !Ready(one),
            "14N matching test-only catalog still cannot alter empty production catalog");

        var two = new ServiceCollection();
        two.AddScoped<IToolHandler, ReadFixtureHandler>();
        two.AddScoped<IToolHandler, WriteFixtureHandler>();
        check(!Ready(two) && Matches(two, [Read, Write]),
            "14N multiple unreviewed handlers remain false despite valid test-only exact set");
        check(!Matches(two, [Read]), "14N unreviewed extra handler cannot pass exact-set rule");
        check(!Matches(two, [Read, Write with { ToolId = Read.ToolId }]),
            "14N duplicate reviewed tool identity cannot count");
        check(!Matches(two, [Read, Write with { HandlerType = Read.HandlerType }]),
            "14N duplicate reviewed handler type cannot count");
        check(!Matches(two, [Read, Write with { Permission = ToolPermissionRequirement.OwnedProjectRead }]),
            "14N capability and permission mismatch cannot count");
        check(!Matches(two, [Read with { ToolId = " REVIEWED-READ " }, Write]),
            "14N noncanonical reviewed tool identity cannot count");

        var duplicate = new ServiceCollection();
        duplicate.AddScoped<IToolHandler, ReadFixtureHandler>();
        duplicate.AddScoped<IToolHandler, ReadFixtureHandler>();
        check(!Ready(duplicate) && !Matches(duplicate, [Read]),
            "14N duplicate handler registration cannot count");
        var factory = new ServiceCollection();
        factory.AddScoped<IToolHandler>(_ => throw new InvalidOperationException());
        check(!Ready(factory) && !Matches(factory, [Read]),
            "14N factory registration cannot count");
        var instance = new ServiceCollection();
        instance.AddSingleton<IToolHandler>(new ReadFixtureHandler());
        check(!Ready(instance) && !Matches(instance, [Read]),
            "14N instance registration cannot count");
        var singleton = new ServiceCollection();
        singleton.AddSingleton<IToolHandler, ReadFixtureHandler>();
        check(!Ready(singleton) && !Matches(singleton, [Read]),
            "14N singleton handler cannot count");
        var transient = new ServiceCollection();
        transient.AddTransient<IToolHandler, ReadFixtureHandler>();
        check(!Ready(transient) && !Matches(transient, [Read]),
            "14N transient handler cannot count");

        var reviewed = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedToolHandlers",
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as
            IReadOnlyList<(Type HandlerType, string ToolId, ToolCapability Capability,
                ToolPermissionRequirement Permission)>;
        check(reviewed is { Count: 0 } && !Ready(new ServiceCollection()),
            "14N source-controlled production catalog remains empty");

        var productionHandlers = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IToolHandler).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract &&
                typeof(IToolHandler).IsAssignableFrom(type)).ToArray();
        check(productionHandlers.Length == 0 && !Ready(new ServiceCollection()),
            "14N production assemblies contain no concrete tool handler");

        var otherCapabilities = new ServiceCollection();
        otherCapabilities.AddScoped<IToolExecutionPolicy, DenyToolExecutionPolicy>();
        otherCapabilities.AddScoped<ITrustedToolExecutionService, TrustedToolExecutionService>();
        check(!Ready(otherCapabilities),
            "14N policy and trusted execution presence cannot substitute for handler set");

        var root = Directory.GetCurrentDirectory();
        string ReadSource(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var inspector = ReadSource("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var api = ReadSource("Aura.Api", "Program.cs");
        var contracts = ReadSource("Aura.Application", "Tools", "ToolContracts.cs");
        var registry = ReadSource("Aura.Application", "Tools", "ToolRegistry.cs");
        var execution = ReadSource("Aura.Application", "Executions", "TrustedToolExecutionService.cs");

        check(inspector.Contains("MatchesReviewedToolHandlers(services, ReviewedToolHandlers)",
                StringComparison.Ordinal) &&
            inspector.Contains("reviewed.Count is 0 or > ToolLimits.MaximumTools", StringComparison.Ordinal) &&
            inspector.Contains("registrations.Length != reviewed.Count", StringComparison.Ordinal) &&
            inspector.Contains("descriptor.ImplementationType == item.HandlerType", StringComparison.Ordinal) &&
            inspector.Contains("descriptor.Lifetime == ServiceLifetime.Scoped", StringComparison.Ordinal) &&
            !inspector.Contains("Assembly.GetTypes", StringComparison.Ordinal) &&
            !inspector.Contains("IsAssignableFrom", StringComparison.Ordinal),
            "14N exact count, type and scoped lifetime use no production reflection discovery");
        check(contracts.Contains("public enum ToolCapability", StringComparison.Ordinal) &&
            contracts.Contains("public enum ToolPermissionRequirement", StringComparison.Ordinal) &&
            contracts.Contains("public bool RequiresApproval => true", StringComparison.Ordinal) &&
            contracts.Contains("public bool RequiresOwnedScope => true", StringComparison.Ordinal) &&
            contracts.Contains("public bool Enabled => false", StringComparison.Ordinal) &&
            inspector.Contains("ToolIdentifier.TryCreate(item.ToolId", StringComparison.Ordinal),
            "14N explicit canonical tool ID, capability and permission metadata retain deny defaults");
        check(registry.Contains("registrations.TryAdd(tool.Descriptor.Id, tool)", StringComparison.Ordinal) &&
            registry.Contains("return false", StringComparison.Ordinal) &&
            registry.Contains("ToolExecutionStatus.Disabled", StringComparison.Ordinal) &&
            registry.Contains("ToolExecutionStatus.UnknownTool", StringComparison.Ordinal) &&
            !registry.Contains("Process.Start", StringComparison.Ordinal) &&
            !registry.Contains("File.Read", StringComparison.Ordinal),
            "14N registry rejects duplicate IDs and dispatcher has no unknown-tool or file/process fallback");
        check(execution.Contains("ApprovalValidation.IsBoundToApprovedAction", StringComparison.Ordinal) &&
            execution.Contains("permissions.HasPermissionAsync", StringComparison.Ordinal) &&
            execution.Contains("policy.AllowsAsync", StringComparison.Ordinal) &&
            execution.IndexOf("policy.AllowsAsync", StringComparison.Ordinal) <
                execution.IndexOf("handler.ExecuteAsync", StringComparison.Ordinal) &&
            execution.Contains("catch (OperationCanceledException) { throw; }", StringComparison.Ordinal),
            "14N handler path retains approval, permission, policy and cancellation gates");
        check(!api.Contains("AddScoped<IToolHandler", StringComparison.Ordinal) &&
            !api.Contains("AddSingleton<IToolHandler", StringComparison.Ordinal) &&
            !api.Contains("AddTransient<IToolHandler", StringComparison.Ordinal) &&
            !api.Contains("ToolRegistry(", StringComparison.Ordinal) &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal),
            "14N normal API has no handler set, registry or readiness claim");

        var partial = new ProductionRuntimeCompositionSnapshot(true, true, true, true, true,
            true, true, true, false, true);
        check(!partial.ToolHandlers && !partial.CompositionComplete && !Ready(otherCapabilities),
            "14N missing handler set blocks composition even when all other fields are true");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, SafeFileAccessEnabled = true };
        check(flags.ToolExecutionEnabled && !Ready(new ServiceCollection()) &&
            !new RuntimeSafetyOptions().SafeFileAccessEnabled,
            "14N flags cannot make handlers ready or enable Safe File Access by default");
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
            "14N introduces no worker, scheduler, migration, live DB, provider, file or tool call");
        var defaults = new RuntimeSafetyOptions();
        check(!Ready(new ServiceCollection()) &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled,
            "14N production execution and optional capabilities remain disabled");
        return Task.CompletedTask;
    }

    private sealed class ReadFixtureHandler : IToolHandler
    {
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Never invoked.");
    }

    private sealed class WriteFixtureHandler : IToolHandler
    {
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Never invoked.");
    }
}
