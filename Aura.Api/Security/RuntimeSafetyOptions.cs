using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.Security;
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

        // Derived capabilities cannot be asserted without their durable source.
        if (readiness.Reconciliation && !readiness.ExecutionState ||
            readiness.Verification && !readiness.ExecutionState)
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
            options.SafeFileAccessEnabled && (!readiness.SafeFileAccess ||
                !HasConfiguredFileRoots(fileAccess)))
            return RuntimeSafetyStatus.MissingRequiredAdapter;

        if (options.CloudAiEnabled && !OpenAiConfiguration.TryValidate(cloud))
            return RuntimeSafetyStatus.InvalidConfiguration;

        return options.ProductionAdaptersEnabled || options.ToolExecutionEnabled ||
            options.SafeFileAccessEnabled || options.CloudAiEnabled || options.AuditPersistenceEnabled
            ? RuntimeSafetyStatus.Valid : RuntimeSafetyStatus.SafeDisabled;
    }

    private static bool HasConfiguredFileRoots(ProjectFileAccessOptions options)
    {
        try
        {
            return options.AllowedRootsByUser is { Count: > 0 } roots && roots.All(entry =>
                Guid.TryParseExact(entry.Key, "D", out var userId) && userId != Guid.Empty &&
                entry.Value is { Length: > 0 } && entry.Value.All(root =>
                    !string.IsNullOrWhiteSpace(root) && Path.IsPathFullyQualified(root) &&
                    (!OperatingSystem.IsWindows() || !root.StartsWith(@"\\", StringComparison.Ordinal)) &&
                    !string.Equals(Path.GetFullPath(root), Path.GetPathRoot(root),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
