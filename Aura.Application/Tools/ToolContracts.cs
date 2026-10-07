namespace Aura.Application.Tools;

public static class ToolLimits
{
    public const int MaximumTools = 32;
    public const int MaximumIdCharacters = 64;
    public const int MaximumDisplayNameCharacters = 80;
    public const int MaximumDescriptionCharacters = 256;
    public const int MaximumResultMessageCharacters = 80;
}

public sealed record ToolIdentifier
{
    public string Value { get; }
    private ToolIdentifier(string value) => Value = value;

    public static bool TryCreate(string? value, out ToolIdentifier? identifier)
    {
        identifier = null;
        // Bound original input before normalization or allocation. No paths/URLs/type names.
        if (value is null || value.Length is < 1 or > ToolLimits.MaximumIdCharacters) return false;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0 || !char.IsAsciiLetter(normalized[0]) ||
            normalized.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return false;
        identifier = new(normalized);
        return true;
    }
}

// Classification metadata only; none of these values grants access.
public enum ToolCapability { ProjectRead, ProjectWrite, ExternalAction }
public enum ToolPermissionRequirement { OwnedProjectRead, OwnedProjectWrite, ExternalAction }

public sealed record ToolDescriptor
{
    public ToolIdentifier Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public ToolCapability Capability { get; }
    public ToolPermissionRequirement RequiredPermission { get; }
    public bool RequiresApproval => true;
    public bool RequiresOwnedScope => true;
    public bool Enabled => false;

    public ToolDescriptor(ToolIdentifier id, string displayName, string description,
        ToolCapability capability, ToolPermissionRequirement requiredPermission)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!ValidText(displayName, ToolLimits.MaximumDisplayNameCharacters) ||
            !ValidText(description, ToolLimits.MaximumDescriptionCharacters) ||
            !Enum.IsDefined(capability) || !Enum.IsDefined(requiredPermission))
            throw new ArgumentException("Invalid tool descriptor.");
        Id = id;
        DisplayName = displayName;
        Description = description;
        Capability = capability;
        RequiredPermission = requiredPermission;
    }

    private static bool ValidText(string? value, int maximum) => value is not null &&
        value.Length <= maximum && !string.IsNullOrWhiteSpace(value) &&
        value.All(c => c is >= ' ' and <= '~');
}

// No arguments or authorization flags. Tool-specific schemas and server-owned scope are deferred.
public sealed record ToolInvocationRequest(ToolIdentifier ToolId);
public enum ToolExecutionStatus { Success, Disabled, UnknownTool, InvalidRequest, PermissionDenied, ApprovalRequired, Failed }

public sealed record ToolExecutionResult
{
    public ToolExecutionStatus Status { get; }
    public string Message { get; }
    private ToolExecutionResult(ToolExecutionStatus status, string message) { Status = status; Message = message; }

    // Only fixed application messages can cross the boundary; no arbitrary handler output/errors.
    public static ToolExecutionResult FromStatus(ToolExecutionStatus status) => status switch
    {
        ToolExecutionStatus.Success => new(status, "Tool completed."),
        ToolExecutionStatus.Disabled => new(status, "Tool execution is disabled."),
        ToolExecutionStatus.UnknownTool => new(status, "Tool is unavailable."),
        ToolExecutionStatus.InvalidRequest => new(status, "Invalid tool request."),
        ToolExecutionStatus.PermissionDenied => new(status, "Tool permission denied."),
        ToolExecutionStatus.ApprovalRequired => new(status, "Tool approval required."),
        _ => new(ToolExecutionStatus.Failed, "Tool execution failed.")
    };
}

public interface IToolHandler
{
    // FUTURE authorized action workflow only: permission -> user approval -> execute -> verify.
    // Registration, request data and AI text never constitute authorization. Implementations
    // must propagate cancellation and map failures to fixed results, never raw exceptions.
    Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken cancellationToken);
}

public interface IToolRegistry
{
    IReadOnlyList<ToolDescriptor> Descriptors { get; }
    bool TryResolve(ToolIdentifier id, out ToolDescriptor? descriptor, out IToolHandler? handler);
}

public interface IToolDispatcher
{
    Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest? request, CancellationToken cancellationToken = default);
}
