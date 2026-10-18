using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Executions;

namespace Aura.Application.Tools;

// Project-only identity. This is a resource binding, never an authorization grant.
public sealed record OwnedProjectTarget
{
    public Guid ProjectId { get; }

    private OwnedProjectTarget(Guid projectId) => ProjectId = projectId;

    internal static OwnedProjectTarget? FromReserved(ActionDescriptor? action,
        ApprovalEvidence? approval, ActionExecutionState? reserved)
    {
        if (action is null || approval is null || reserved is null ||
            action.Scope.ProjectId == Guid.Empty ||
            !ApprovalValidation.IsBoundToApprovedAction(approval, action,
                action.Scope, action.ToolId) ||
            reserved.Status != ActionExecutionStateStatus.Reserved || reserved.Version != 1 ||
            reserved.ExecutionId is not Guid executionId || executionId == Guid.Empty ||
            reserved.ActionId != action.Id || reserved.Scope != action.Scope ||
            reserved.ToolId != action.ToolId || reserved.ApprovalId != approval.Id.Value ||
            reserved.Scope.ProjectId != action.Scope.ProjectId)
            return null;
        return new(action.Scope.ProjectId);
    }
}
