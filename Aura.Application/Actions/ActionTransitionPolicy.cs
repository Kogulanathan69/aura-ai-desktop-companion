namespace Aura.Application.Actions;

public static class ActionTransitionPolicy
{
    // Graph shape only. A true result is NOT authorization or approval evidence.
    public static bool IsDefinedTransition(ActionLifecycleStatus current, ActionLifecycleStatus target) => (current, target) switch
    {
        (ActionLifecycleStatus.Proposed, ActionLifecycleStatus.ApprovalRequired or ActionLifecycleStatus.Cancelled) => true,
        (ActionLifecycleStatus.ApprovalRequired, ActionLifecycleStatus.Approved or ActionLifecycleStatus.Rejected or ActionLifecycleStatus.Cancelled) => true,
        (ActionLifecycleStatus.Approved, ActionLifecycleStatus.Executing or ActionLifecycleStatus.Cancelled) => true,
        (ActionLifecycleStatus.Executing, ActionLifecycleStatus.Succeeded or ActionLifecycleStatus.Failed) => true,
        _ => false
    };

    public static ActionOperationStatus Evaluate(ActionLifecycleStatus current, ActionLifecycleStatus target)
    {
        if (!IsDefinedTransition(current, target)) return ActionOperationStatus.TransitionDenied;
        if (target == ActionLifecycleStatus.Approved) return ActionOperationStatus.ApprovalRequired;
        if (target is ActionLifecycleStatus.Executing or ActionLifecycleStatus.Succeeded or ActionLifecycleStatus.Failed)
            return ActionOperationStatus.ExecutionDisabled;
        return ActionOperationStatus.Success;
    }
}
