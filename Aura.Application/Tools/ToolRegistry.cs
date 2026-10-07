namespace Aura.Application.Tools;

public sealed record ToolRegistration(ToolDescriptor Descriptor, IToolHandler Handler);

public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<ToolIdentifier, ToolRegistration> registrations = new();
    public IReadOnlyList<ToolDescriptor> Descriptors { get; }

    public ToolRegistry(IEnumerable<ToolRegistration> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        // Consume at most 33 entries, rejecting the first beyond the fixed registry limit.
        foreach (var tool in tools)
        {
            if (registrations.Count == ToolLimits.MaximumTools)
                throw new ArgumentException("Tool registry limit exceeded.");
            if (tool?.Descriptor is null || tool.Handler is null ||
                !registrations.TryAdd(tool.Descriptor.Id, tool))
                throw new ArgumentException("Invalid or duplicate tool registration.");
        }
        Descriptors = Array.AsReadOnly(registrations.Values.Select(x => x.Descriptor)
            .OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray());
    }

    public bool TryResolve(ToolIdentifier id, out ToolDescriptor? descriptor, out IToolHandler? handler)
    {
        descriptor = null;
        handler = null;
        if (id is null || !registrations.TryGetValue(id, out var registration)) return false;
        descriptor = registration.Descriptor;
        handler = registration.Handler;
        return true;
    }
}

// Deliberately no enabled branch, approval flags, execution calls, retries or fallback.
// This is a denial boundary, not the future authorized action coordinator.
public sealed class DisabledToolDispatcher(ToolRegistry registry) : IToolDispatcher
{
    public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest? request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = request?.ToolId is null ? ToolExecutionStatus.InvalidRequest :
            registry.TryResolve(request.ToolId, out _, out _) ? ToolExecutionStatus.Disabled : ToolExecutionStatus.UnknownTool;
        return Task.FromResult(ToolExecutionResult.FromStatus(status));
    }
}
