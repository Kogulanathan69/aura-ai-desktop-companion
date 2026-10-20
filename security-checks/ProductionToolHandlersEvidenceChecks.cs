using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionToolHandlersEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var method = typeof(IToolHandler).GetMethod(nameof(IToolHandler.ExecuteAsync));
        check(typeof(IToolHandler).GetMethods().Length == 1 && method?.ReturnType == typeof(Task<ToolExecutionResult>) &&
            method.GetParameters().Select(p => p.ParameterType)
                .SequenceEqual([typeof(ToolInvocationRequest), typeof(CancellationToken)]),
            "15I handler contract accepts bounded request and cancellation");
        check(ToolLimits.MaximumTools == 32 && ToolLimits.MaximumIdCharacters == 64 &&
            ToolLimits.MaximumDisplayNameCharacters == 80 && ToolLimits.MaximumDescriptionCharacters == 256 &&
            typeof(ToolDescriptor).GetProperties().All(p => p.SetMethod is null) &&
            typeof(ToolIdentifier).GetProperties().All(p => p.SetMethod is null),
            "15I descriptor and identifier remain bounded and immutable");
        check(Enum.GetValues<ToolCapability>().SequenceEqual(
                [ToolCapability.ProjectRead, ToolCapability.ProjectWrite, ToolCapability.ExternalAction]) &&
            Enum.GetValues<ToolPermissionRequirement>().SequenceEqual(
                [ToolPermissionRequirement.OwnedProjectRead, ToolPermissionRequirement.OwnedProjectWrite,
                    ToolPermissionRequirement.ExternalAction]),
            "15I capability and required-permission metadata remain bounded enums");
        ToolIdentifier.TryCreate(" READ-ITEM ", out var id);
        check(id?.Value == "read-item" &&
            !ToolIdentifier.TryCreate("https://example.test/tool", out _) &&
            !ToolIdentifier.TryCreate("a/path", out _) &&
            !ToolIdentifier.TryCreate("1invalid", out _) &&
            !ToolIdentifier.TryCreate(new string('a', 65), out _),
            "15I canonical tool IDs normalize and reject URL, path, malformed and oversized values");
        var malformed = false;
        try { _ = new ToolDescriptor(id!, "Item", "Fixture", (ToolCapability)999,
            ToolPermissionRequirement.OwnedProjectRead); }
        catch (ArgumentException) { malformed = true; }
        check(malformed, "15I malformed capability descriptor is rejected");
        malformed = false;
        try { _ = new ToolDescriptor(id!, "Item", "Fixture", ToolCapability.ProjectRead,
            (ToolPermissionRequirement)999); }
        catch (ArgumentException) { malformed = true; }
        check(malformed, "15I malformed permission descriptor is rejected");

        // Reuse the existing private security-check fixture; no handler is added to production or this file.
        var fixtureType = typeof(ToolChecks).GetNestedType("FixtureHandler", BindingFlags.NonPublic)!;
        var handler = (IToolHandler)Activator.CreateInstance(fixtureType, nonPublic: true)!;
        var descriptor = new ToolDescriptor(id!, "Item", "Fixture", ToolCapability.ProjectRead,
            ToolPermissionRequirement.OwnedProjectRead);
        var registry = new ToolRegistry([new(descriptor, handler)]);
        ToolIdentifier.TryCreate("unknown", out var unknown);
        check(registry.TryResolve(id!, out var found, out var resolved) &&
            ReferenceEquals(found, descriptor) && ReferenceEquals(resolved, handler),
            "15I registry resolves only the explicit descriptor and handler instance");
        check(!registry.TryResolve(unknown!, out found, out resolved) &&
            found is null && resolved is null,
            "15I unknown ID has no registry fallback");
        malformed = false;
        try { _ = new ToolRegistry([new(descriptor, handler), new(descriptor, handler)]); }
        catch (ArgumentException) { malformed = true; }
        check(malformed, "15I duplicate normalized tool ID is rejected");
        ToolIdentifier.TryCreate("second", out var second);
        check(new ToolRegistry([new(descriptor, handler),
            new(new ToolDescriptor(second!, "Second", "Fixture", ToolCapability.ProjectRead,
                ToolPermissionRequirement.OwnedProjectRead), handler)]).Descriptors.Count == 2,
            "15I runtime registry allows one handler instance for multiple IDs; production exact-set review must reject duplicate handler type");
        var dispatcher = new DisabledToolDispatcher(registry);
        check((await dispatcher.ExecuteAsync(new(unknown!))).Status ==
                ToolExecutionStatus.UnknownTool &&
            (await dispatcher.ExecuteAsync(new(id!))).Status == ToolExecutionStatus.Disabled,
            "15I disabled dispatcher never executes known or unknown handlers");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var propagated = false;
            try { await dispatcher.ExecuteAsync(new(id!), cancelled.Token); }
            catch (OperationCanceledException) { propagated = true; }
            check(propagated, "15I dispatcher propagates cancellation before handler");
        }
        check(typeof(ToolInvocationRequest).GetProperties().Select(p => p.Name)
                .ToHashSet().SetEquals(["ToolId", "Target"]) &&
            typeof(ToolInvocationRequest).GetProperties().All(p => p.SetMethod is null) &&
            typeof(ToolExecutionResult).GetConstructors().Length == 0 &&
            typeof(ToolExecutionResult).GetProperties().Select(p => p.Name).SequenceEqual(["Status", "Message"]) &&
            typeof(ToolExecutionResult).GetProperties().All(p => p.SetMethod is null) &&
            Enum.GetValues<ToolExecutionStatus>().All(status =>
                ToolExecutionResult.FromStatus(status).Message.Length <= ToolLimits.MaximumResultMessageCharacters) &&
            ToolExecutionResult.FromStatus((ToolExecutionStatus)999).Status == ToolExecutionStatus.Failed,
            "15I request has no arbitrary command/path/URL and result has only fixed bounded messages");

        var productionHandlers = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IToolHandler).Assembly.GetTypes())
            .Concat(typeof(ProductionRuntimeCompositionInspector).Assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IToolHandler).IsAssignableFrom(t)).ToArray();
        var testHandlers = typeof(ToolChecks).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IToolHandler).IsAssignableFrom(t)).ToArray();
        check(productionHandlers.Length == 0 && testHandlers.Length >= 10 &&
            testHandlers.Contains(fixtureType),
            "15I concrete handlers exist only as security-check fixtures");
        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedToolHandlers",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.ICollection;
        check(catalog?.Count == 0, "15I reviewed production handler catalog is empty");
        var synthetic = new ServiceCollection();
        synthetic.AddScoped(typeof(IToolHandler), fixtureType);
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(synthetic);
        check(!snapshot.ToolHandlers && !snapshot.CompositionComplete &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Handler,
            "15I test-only registration and runtime flags cannot create handler readiness");

        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var execution = Source("Aura.Application", "Executions", "TrustedToolExecutionService.cs");
        var registrySource = Source("Aura.Application", "Tools", "ToolRegistry.cs");
        var entry = Source("security-checks", "Program.cs");
        check(entry.Contains("await ProductionToolHandlersEvidenceChecks.RunAsync(Check)") &&
            !api.Contains("AddScoped<IToolHandler") && !api.Contains("AddSingleton<IToolHandler") &&
            !api.Contains("AddTransient<IToolHandler") &&
            !api.Contains("AddScoped<ToolRegistry") && !api.Contains("AddSingleton<ToolRegistry") &&
            !api.Contains("AddScoped<IToolDispatcher") && !api.Contains("AddSingleton<IToolDispatcher") &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()"),
            "15I normal API registers no handler, registry or dispatcher");
        check(inspector.Contains("ReviewedToolHandlers =") &&
            inspector.Contains("Array.Empty<(Type, string, ToolCapability, ToolPermissionRequirement)>()") &&
            inspector.Contains("reviewed.Count is 0 or > ToolLimits.MaximumTools") &&
            inspector.Contains("registrations.Length != reviewed.Count") &&
            inspector.Contains("Distinct(StringComparer.Ordinal)") &&
            inspector.Contains("descriptor.ImplementationType == item.HandlerType") &&
            inspector.Contains("descriptor.Lifetime == ServiceLifetime.Scoped") &&
            inspector.Contains("ToolIdentifier.TryCreate(item.ToolId") &&
            !inspector.Contains("Assembly.GetTypes") && !inspector.Contains("Activator.CreateInstance"),
            "15I exact reviewed set requires unique type/ID, canonical metadata and scoped registration without discovery");
        check(registrySource.Contains("registrations.TryAdd(tool.Descriptor.Id, tool)") &&
            registrySource.Contains("ToolExecutionStatus.UnknownTool") &&
            !registrySource.Contains("Assembly.Load") && !registrySource.Contains("GetTypes(") &&
            !registrySource.Contains("Plugin") && !api.Contains("Assembly.Load"),
            "15I explicit registry has no unknown-tool fallback or production plugin discovery");
        check(execution.Contains("ApprovalValidation.IsBoundToApprovedAction") &&
            execution.Contains("scopes.ValidateActionAsync(exactAction, cancellationToken)") &&
            execution.Contains("permissions.HasPermissionAsync(scope, tool, cancellationToken)") &&
            execution.Contains("policy.AllowsAsync(tool, cancellationToken)") &&
            execution.Contains("states.TryReserveInitialAsync(ready, attempt, cancellationToken)") &&
            execution.IndexOf("states.TryReserveInitialAsync", StringComparison.Ordinal) <
                execution.IndexOf("handler.ExecuteAsync", StringComparison.Ordinal) &&
            execution.Contains("catch (OperationCanceledException) { throw; }") &&
            execution.Contains("catch (Exception) { handlerResult = ToolExecutionResult.FromStatus(ToolExecutionStatus.Failed); }") &&
            !execution.Contains("Task.Delay") && !execution.Contains("while (") &&
            execution.Split("handler.ExecuteAsync").Length == 2,
            "15I handler call requires approval, ownership, permission, policy and durable reservation; fixed exception and cancellation handling, no retry");
    }

}
