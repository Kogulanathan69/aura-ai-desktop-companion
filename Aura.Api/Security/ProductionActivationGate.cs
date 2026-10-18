using Aura.Application.Security;

namespace Aura.Api.Security;

// Approval must eventually come from an independently reviewed source, not a flag.
public interface IFinalProductionSecurityApprovalSource
{
    bool IsApproved { get; }
}

public sealed class UnavailableFinalProductionSecurityApprovalSource : IFinalProductionSecurityApprovalSource
{
    public bool IsApproved => false;
}

// Pure snapshot evaluator. It never resolves or invokes a tool or changes runtime flags.
public sealed class ProductionActivationGate
{
    public bool CanActivate(ProductionRuntimeCompositionSnapshot composition,
        RuntimeSafetyOptions flags, RuntimeCapabilitySnapshot readiness,
        RuntimeSafetyStatus safetyStatus, IFinalProductionSecurityApprovalSource approval,
        bool cloudProviderReady = false)
    {
        if (composition is null || flags is null || readiness is null || approval is null)
            return false;
        return composition.CompositionComplete &&
            flags.ProductionAdaptersEnabled && flags.ToolExecutionEnabled &&
            flags.AuditPersistenceEnabled &&
            readiness.Ownership && readiness.Permission && readiness.ExecutionState &&
            readiness.AuditSink && readiness.Reconciliation && readiness.Verification &&
            readiness.Handler && readiness.ExecutionPolicy &&
            (!flags.SafeFileAccessEnabled || readiness.SafeFileAccess) &&
            (!flags.CloudAiEnabled || cloudProviderReady) &&
            safetyStatus == RuntimeSafetyStatus.Valid && approval.IsApproved;
    }
}
