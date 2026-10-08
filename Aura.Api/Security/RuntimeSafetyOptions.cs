using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;
using Aura.Infrastructure.AI.OpenAI;

namespace Aura.Api.Security;

// Startup-only coordination; existing provider and file options remain authoritative.
public sealed class RuntimeSafetyOptions
{
    public const string SectionName = "Security:RuntimeSafety";
    public bool ProductionAdaptersEnabled { get; init; } = false;
    public bool ToolExecutionEnabled { get; init; } = false;
    public bool SafeFileAccessEnabled { get; init; } = false;
    public bool CloudAiEnabled { get; init; } = false;
    public bool AuditPersistenceEnabled { get; init; } = false;
}

// Explicit readiness claims must come from future reviewed registrations, not DI inspection.
public sealed record RuntimeCapabilitySnapshot(bool Ownership, bool Permission, bool ExecutionState,
    bool AuditSink, bool Reconciliation, bool Verification, bool Handler, bool ExecutionPolicy,
    bool SafeFileAccess);

public interface IRuntimeCapabilityReadiness
{
    RuntimeCapabilitySnapshot GetSnapshot();
}

public sealed class UnavailableRuntimeCapabilityReadiness : IRuntimeCapabilityReadiness
{
    public RuntimeCapabilitySnapshot GetSnapshot() => new(false, false, false, false, false,
        false, false, false, false);
}

public enum RuntimeSafetyStatus { SafeDisabled, Valid, InvalidConfiguration, MissingRequiredAdapter, UnsafeCombination }

public sealed class RuntimeSafetyValidator
{
    public const string FailureMessage = "Runtime safety configuration is invalid.";

    public RuntimeSafetyStatus Validate(RuntimeSafetyOptions? options, RuntimeCapabilitySnapshot? readiness,
        ProjectFileAccessOptions? fileAccess, AiProviderRouterOptions? router, OpenAiOptions? cloud)
    {
        if (options is null || readiness is null || fileAccess is null || router is null || cloud is null ||
            !Enum.IsDefined(router.Mode))
            return RuntimeSafetyStatus.InvalidConfiguration;

        // Existing options cannot silently enable a capability omitted from this section.
        if (fileAccess.Enabled != options.SafeFileAccessEnabled ||
            cloud.Enabled != options.CloudAiEnabled ||
            (router.Mode == AiProviderMode.Cloud) != options.CloudAiEnabled)
            return RuntimeSafetyStatus.UnsafeCombination;

        if (options.ToolExecutionEnabled && !options.ProductionAdaptersEnabled ||
            options.AuditPersistenceEnabled && !options.ProductionAdaptersEnabled)
            return RuntimeSafetyStatus.UnsafeCombination;

        if (options.ProductionAdaptersEnabled &&
            !(readiness.Ownership && readiness.Permission && readiness.ExecutionState &&
              readiness.AuditSink && readiness.Reconciliation && readiness.Verification) ||
            options.ToolExecutionEnabled &&
            !(readiness.Handler && readiness.ExecutionPolicy && readiness.Ownership &&
              readiness.Permission && readiness.ExecutionState && readiness.AuditSink) ||
            options.AuditPersistenceEnabled && !readiness.AuditSink ||
            options.SafeFileAccessEnabled && !readiness.SafeFileAccess)
            return RuntimeSafetyStatus.MissingRequiredAdapter;

        return options.ProductionAdaptersEnabled || options.ToolExecutionEnabled ||
            options.SafeFileAccessEnabled || options.CloudAiEnabled || options.AuditPersistenceEnabled
            ? RuntimeSafetyStatus.Valid : RuntimeSafetyStatus.SafeDisabled;
    }
}
