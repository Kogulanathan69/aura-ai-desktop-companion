using Aura.Api.Security;
using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.Security;
using Aura.Infrastructure.AI.OpenAI;
using Aura.Infrastructure.Data;

internal static class RuntimeReadinessConsolidationChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var defaults = new RuntimeSafetyOptions();
        var validator = new RuntimeSafetyValidator();
        var files = new ProjectFileAccessOptions();
        var router = new AiProviderRouterOptions();
        var cloud = new OpenAiOptions();
        RuntimeSafetyStatus Assess(RuntimeSafetyOptions options, RuntimeCapabilitySnapshot? readiness = null,
            ProjectFileAccessOptions? fileAccess = null, AiProviderRouterOptions? route = null,
            OpenAiOptions? provider = null) => validator.Validate(options, readiness ?? startup,
                fileAccess ?? files, route ?? router, provider ?? cloud);

        check(!startup.Ownership, "12K startup ownership unavailable");
        check(!startup.Permission, "12K startup permission unavailable");
        check(!startup.ExecutionState, "12K startup execution state unavailable");
        check(!startup.AuditSink, "12K startup audit sink unavailable");
        check(!startup.Reconciliation, "12K startup reconciliation unavailable");
        check(!startup.Verification, "12K startup verification unavailable");
        check(informational.Ownership && !startup.Ownership &&
            !informational.Permission && !informational.ExecutionState && !informational.AuditSink &&
            !informational.Reconciliation && !informational.Verification,
            "12K informational ownership implementation does not inflate runtime readiness");
        check(!informational.Handler && !informational.ExecutionPolicy && !informational.SafeFileAccess,
            "12K informational persistence snapshot claims no unrelated runtime capability");

        check(!defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.AuditPersistenceEnabled && !defaults.SafeFileAccessEnabled &&
            !defaults.CloudAiEnabled && Assess(defaults) == RuntimeSafetyStatus.SafeDisabled,
            "12K all production toggles default disabled");
        var execute = new RuntimeSafetyOptions { ToolExecutionEnabled = true };
        check(Assess(execute) == RuntimeSafetyStatus.UnsafeCombination,
            "12K tool execution without production gate rejected");
        var production = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true };
        var productionExecution = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true };
        check(Assess(productionExecution) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K tool execution without ownership, permission, and state rejected");
        var ready = new RuntimeCapabilitySnapshot(true, true, true, true, true, true, true, true, false);
        check(Assess(productionExecution, ready with { Ownership = false }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K tool execution requires ownership");
        check(Assess(productionExecution, ready with { Permission = false }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K tool execution requires permission");
        check(Assess(productionExecution, ready with { ExecutionState = false, Reconciliation = false,
            Verification = false }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K tool execution requires durable execution state");
        check(Assess(productionExecution, ready with { Handler = false }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K tool execution requires a handler");
        check(Assess(productionExecution, ready with { ExecutionPolicy = false }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K tool execution requires policy");
        check(Assess(productionExecution, ready with { AuditSink = false }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K production tool execution requires durable audit sink readiness");
        check(Assess(productionExecution, ready) == RuntimeSafetyStatus.Valid,
            "12K complete explicit readiness is necessary for production tool execution");
        check(Assess(production, startup with { Ownership = true }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K ownership alone cannot enable production adapters");
        check(Assess(production, startup with { Permission = true }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K permission alone cannot enable production adapters");
        check(Assess(production, startup with { AuditSink = true }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K audit alone cannot enable production adapters");
        check(Assess(production, startup with { Verification = true, ExecutionState = true }) ==
            RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K verification alone cannot enable production adapters");
        check(Assess(production, ready with { Reconciliation = false }) == RuntimeSafetyStatus.MissingRequiredAdapter &&
            Assess(production, ready with { Verification = false }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K production gate requires reconciliation and verification independently");

        var audit = new RuntimeSafetyOptions { AuditPersistenceEnabled = true };
        check(Assess(audit) == RuntimeSafetyStatus.UnsafeCombination,
            "12K audit persistence requires production gate");
        check(Assess(new RuntimeSafetyOptions { AuditPersistenceEnabled = true,
            ProductionAdaptersEnabled = true }, ready with { AuditSink = false }) ==
            RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K audit persistence requires independently ready sink");
        check(Assess(defaults, startup with { AuditSink = true }) == RuntimeSafetyStatus.SafeDisabled &&
            Assess(defaults, startup with { Ownership = true }) == RuntimeSafetyStatus.SafeDisabled,
            "12K audit and ownership assertions do not enable one another");
        check(Assess(defaults, startup with { Reconciliation = true }) == RuntimeSafetyStatus.InvalidConfiguration &&
            Assess(defaults, startup with { Verification = true }) == RuntimeSafetyStatus.InvalidConfiguration,
            "12K reconciliation and verification cannot claim readiness without execution state");

        var access = new RuntimeSafetyOptions { SafeFileAccessEnabled = true };
        check(Assess(access) == RuntimeSafetyStatus.UnsafeCombination,
            "12K file access flag without file option rejected");
        check(Assess(access, fileAccess: new ProjectFileAccessOptions { Enabled = true }) ==
            RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K enabled file option without readiness and roots rejected");
        check(Assess(access, startup with { SafeFileAccess = true },
            new ProjectFileAccessOptions { Enabled = true }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K file readiness without configured roots rejected");
        check(Assess(access, startup with { SafeFileAccess = true },
            new ProjectFileAccessOptions { Enabled = true, AllowedRootsByUser = new()
            { [Guid.NewGuid().ToString("D")] = [Path.Combine(Path.GetTempPath(), "synthetic-root")] } }) == RuntimeSafetyStatus.Valid,
            "12K file access requires independent readiness and server-side roots");
        check(Assess(access, startup with { SafeFileAccess = true },
            new ProjectFileAccessOptions { Enabled = true, AllowedRootsByUser = new()
            { [Guid.NewGuid().ToString("D")] = ["relative-root"] } }) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "12K malformed relative file root cannot validate");
        check(Assess(defaults, startup with { Ownership = true }) == RuntimeSafetyStatus.SafeDisabled &&
            !files.Enabled, "12K ownership readiness cannot imply file access");

        var cloudEnabled = new RuntimeSafetyOptions { CloudAiEnabled = true };
        check(Assess(cloudEnabled) == RuntimeSafetyStatus.UnsafeCombination,
            "12K cloud flag without router and provider agreement rejected");
        check(Assess(cloudEnabled, route: new AiProviderRouterOptions { Mode = AiProviderMode.Cloud },
            provider: new OpenAiOptions { Enabled = true }) == RuntimeSafetyStatus.InvalidConfiguration,
            "12K cloud configuration requires valid provider settings");
        check(Assess(defaults, ready) == RuntimeSafetyStatus.SafeDisabled && !cloud.Enabled,
            "12K production readiness assertions do not imply cloud AI enablement");
        check(RuntimeSafetyValidator.FailureMessage == "Runtime safety configuration is invalid." &&
            RuntimeSafetyValidator.FailureMessage.Length <= 120,
            "12K startup failure message remains fixed and bounded");

        // Source boundary check: normal startup must retain the unavailable snapshot and
        // must not call the future-only ownership registration or wire workflow adapters.
        var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Aura.Api", "Program.cs"));
        check(source.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            !source.Contains("new PersistenceCapabilityReadiness()", StringComparison.Ordinal),
            "12K normal startup uses unavailable runtime readiness, not informational persistence");
        check(!source.Contains(".AddOwnershipRuntimeFoundation(", StringComparison.Ordinal) &&
            !source.Contains("AddScoped<ICurrentUserContext", StringComparison.Ordinal) &&
            !source.Contains("AddScoped<IOwnershipSource", StringComparison.Ordinal),
            "12K normal startup does not register production ownership workflow");
        foreach (var contract in new[] { "IToolPermissionSource", "IActionExecutionStateStore", "IAuditEventSink",
            "IExecutionReconciliationSource", "IExecutionEvidenceSource", "ITrustedToolExecutionService" })
            check(!source.Contains("AddScoped<" + contract, StringComparison.Ordinal) &&
                !source.Contains("AddSingleton<" + contract, StringComparison.Ordinal) &&
                !source.Contains("AddTransient<" + contract, StringComparison.Ordinal),
                "12K no production startup registration: " + contract);
        check(!source.Contains("ToolExecutionEnabled = true", StringComparison.Ordinal) &&
            !source.Contains("SafeFileAccessEnabled = true", StringComparison.Ordinal) &&
            !source.Contains("CloudAiEnabled = true", StringComparison.Ordinal),
            "12K normal startup does not force tool, file, or cloud enablement");
        return Task.CompletedTask;
    }
}
