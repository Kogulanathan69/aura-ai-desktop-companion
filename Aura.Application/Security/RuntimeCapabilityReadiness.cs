namespace Aura.Application.Security;

// Explicit capability assertions for future reviewed startup wiring; not DI inspection.
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
