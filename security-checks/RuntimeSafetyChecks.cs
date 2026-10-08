using Aura.Api.Security;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;
using Aura.Infrastructure.AI.OpenAI;
using Microsoft.Extensions.Configuration;

internal static class RuntimeSafetyChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var options = new RuntimeSafetyOptions();
        var readiness = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var validator = new RuntimeSafetyValidator();
        var files = new ProjectFileAccessOptions();
        var router = new AiProviderRouterOptions();
        var cloud = new OpenAiOptions();
        RuntimeSafetyStatus Assess(RuntimeSafetyOptions? value, RuntimeCapabilitySnapshot? capability = null,
            ProjectFileAccessOptions? fileOptions = null, AiProviderRouterOptions? routerOptions = null,
            OpenAiOptions? cloudOptions = null) => validator.Validate(value, capability ?? readiness,
                fileOptions ?? files, routerOptions ?? router, cloudOptions ?? cloud);

        check(RuntimeSafetyOptions.SectionName == "Security:RuntimeSafety" &&
            typeof(RuntimeSafetyOptions).GetProperties().Select(x => x.Name).SequenceEqual([
                "ProductionAdaptersEnabled", "ToolExecutionEnabled", "SafeFileAccessEnabled",
                "CloudAiEnabled", "AuditPersistenceEnabled"]),
            "11Z fixed bounded runtime safety section and boolean fields");
        check(!options.ProductionAdaptersEnabled && !options.ToolExecutionEnabled &&
            !options.SafeFileAccessEnabled && !options.CloudAiEnabled &&
            !options.AuditPersistenceEnabled && Assess(options) == RuntimeSafetyStatus.SafeDisabled,
            "11Z all missing safety options default disabled and validate");
        check(!readiness.Ownership && !readiness.Permission && !readiness.ExecutionState &&
            !readiness.AuditSink && !readiness.Reconciliation && !readiness.Verification &&
            !readiness.Handler && !readiness.ExecutionPolicy && !readiness.SafeFileAccess,
            "11Z default capability readiness is entirely unavailable");
        check(!files.Enabled && !cloud.Enabled && router.Mode == AiProviderMode.Local &&
            typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(["Prompt"]),
            "11Z existing file, cloud, router, and chat defaults remain bounded");

        check(Assess(new RuntimeSafetyOptions { ProductionAdaptersEnabled = true }) ==
            RuntimeSafetyStatus.MissingRequiredAdapter,
            "11Z production adapters cannot enable without complete readiness");
        check(Assess(new RuntimeSafetyOptions { ToolExecutionEnabled = true }) == RuntimeSafetyStatus.UnsafeCombination &&
            Assess(new RuntimeSafetyOptions { ToolExecutionEnabled = true, ProductionAdaptersEnabled = true }) ==
                RuntimeSafetyStatus.MissingRequiredAdapter,
            "11Z execution requires production mode and currently unavailable adapters");
        check(Assess(new RuntimeSafetyOptions { AuditPersistenceEnabled = true }) == RuntimeSafetyStatus.UnsafeCombination &&
            Assess(new RuntimeSafetyOptions { AuditPersistenceEnabled = true, ProductionAdaptersEnabled = true }) ==
                RuntimeSafetyStatus.MissingRequiredAdapter,
            "11Z durable audit cannot enable without production sink");
        check(Assess(new RuntimeSafetyOptions { SafeFileAccessEnabled = true }) == RuntimeSafetyStatus.UnsafeCombination &&
            Assess(new RuntimeSafetyOptions { SafeFileAccessEnabled = true },
                fileOptions: new ProjectFileAccessOptions { Enabled = true }) ==
                RuntimeSafetyStatus.MissingRequiredAdapter &&
            Assess(options, fileOptions: new ProjectFileAccessOptions { Enabled = true }) ==
                RuntimeSafetyStatus.UnsafeCombination,
            "11Z file flag and existing option must agree; missing adapter denies");
        check(Assess(new RuntimeSafetyOptions { CloudAiEnabled = true }) == RuntimeSafetyStatus.UnsafeCombination &&
            Assess(options, routerOptions: new AiProviderRouterOptions { Mode = AiProviderMode.Cloud }) ==
                RuntimeSafetyStatus.UnsafeCombination &&
            Assess(options, cloudOptions: new OpenAiOptions { Enabled = true }) ==
                RuntimeSafetyStatus.UnsafeCombination,
            "11Z partial cloud flag, router, or provider enablement fails closed");
        check(Assess(new RuntimeSafetyOptions { CloudAiEnabled = true },
            routerOptions: new AiProviderRouterOptions { Mode = AiProviderMode.Cloud },
            cloudOptions: new OpenAiOptions { Enabled = true }) == RuntimeSafetyStatus.Valid,
            "11Z deliberate consistent cloud configuration passes pure safety gate");
        check(Assess(options, routerOptions: new AiProviderRouterOptions { Mode = (AiProviderMode)99 }) ==
            RuntimeSafetyStatus.InvalidConfiguration && Assess(null) == RuntimeSafetyStatus.InvalidConfiguration &&
            validator.Validate(options, null, files, router, cloud) == RuntimeSafetyStatus.InvalidConfiguration,
            "11Z unknown router mode and missing inputs fail closed");
        var bound = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Security:RuntimeSafety:ProductionAdaptersEnabled"] = "true" }).Build()
            .GetSection(RuntimeSafetyOptions.SectionName).Get<RuntimeSafetyOptions>();
        check(bound is not null && bound.ProductionAdaptersEnabled &&
            Assess(bound) == RuntimeSafetyStatus.MissingRequiredAdapter,
            "11Z configured enablement is bound but unavailable adapters deny");
        check(RuntimeSafetyValidator.FailureMessage.Length <= 120 &&
            RuntimeSafetyValidator.FailureMessage == "Runtime safety configuration is invalid.",
            "11Z startup failure has fixed bounded safe message");
        return Task.CompletedTask;

    }
}
