namespace Aura.Application.Auditing;

// Conservative event-family rules. Optional IDs are allowed only where named below.
public static class AuditEventValidation
{
    public static AuditWriteStatus Evaluate(AuditWriteRequest? request)
    {
        if (request is null || !Enum.IsDefined(request.EventType) || !Enum.IsDefined(request.Outcome))
            return AuditWriteStatus.InvalidEvent;
        var scope = request.Scope;
        if (scope is null || scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty ||
            scope.ProjectId == Guid.Empty)
            return AuditWriteStatus.InvalidScope;
        var r = request.References;
        if (r is null || r.ActionId is { Value: var actionId } && actionId == Guid.Empty ||
            r.ToolId is { Value: null } || Invalid(r.ApprovalId) || Invalid(r.ExecutionId) ||
            Invalid(r.VerificationId) || Invalid(r.ReconciliationId) || Invalid(r.PermissionGrantId))
            return AuditWriteStatus.InvalidBinding;
        var action = r.ActionId is not null;
        var tool = r.ToolId is not null;
        var approval = r.ApprovalId is not null;
        var execution = r.ExecutionId is not null;
        var verification = r.VerificationId is not null;
        var reconciliation = r.ReconciliationId is not null;
        var grant = r.PermissionGrantId is not null;
        var valid = request.EventType switch
        {
            AuditEventType.ActionProposed or AuditEventType.ApprovalRequested =>
                request.Outcome == AuditEventOutcome.Success && action && tool &&
                !approval && !execution && !verification && !reconciliation && !grant,
            AuditEventType.ApprovalGranted =>
                request.Outcome == AuditEventOutcome.Success && action && tool && approval &&
                !execution && !verification && !reconciliation && !grant,
            AuditEventType.ApprovalRejected =>
                request.Outcome == AuditEventOutcome.Denied && action && tool &&
                !approval && !execution && !verification && !reconciliation && !grant,
            AuditEventType.PermissionAllowed =>
                request.Outcome == AuditEventOutcome.Success && tool && grant &&
                !action && !approval && !execution && !verification && !reconciliation,
            AuditEventType.PermissionDenied =>
                request.Outcome == AuditEventOutcome.Denied && tool &&
                !action && !approval && !execution && !verification && !reconciliation,
            AuditEventType.ExecutionReserved or AuditEventType.ExecutionCompleted =>
                request.Outcome == AuditEventOutcome.Success && action && tool && approval &&
                execution && !verification && !reconciliation && !grant,
            AuditEventType.ExecutionFailed =>
                request.Outcome == AuditEventOutcome.Failed && action && tool && approval &&
                execution && !verification && !reconciliation && !grant,
            AuditEventType.ExecutionReconciliationAssessed =>
                request.Outcome == AuditEventOutcome.Inconclusive && action && execution &&
                reconciliation && !verification && !grant,
            AuditEventType.VerificationAssessed =>
                request.Outcome is AuditEventOutcome.Success or AuditEventOutcome.Failed or
                    AuditEventOutcome.Inconclusive && action && execution && verification &&
                    !reconciliation && !grant,
            AuditEventType.SecurityDenied =>
                request.Outcome == AuditEventOutcome.Denied &&
                !verification && !reconciliation && !grant,
            _ => false
        };
        return valid ? AuditWriteStatus.Success : AuditWriteStatus.InvalidBinding;
    }

    private static bool Invalid(Guid? value) => value == Guid.Empty;
}
