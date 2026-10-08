using Aura.Application.Actions;
using Aura.Application.Tools;

namespace Aura.Application.Executions;

// Internal request only. Scope and observed execution come from trusted application inputs.
public sealed record ExecutionReconciliationRequest(ActionIdentifier ActionId);

public enum ExecutionReconciliationReason
{
    ReservationCancelled, HandlerCancelled, HandlerCompletedButStateIncomplete,
    StoreCompletionUncertain, CoordinatorInterrupted, Unknown
}

// Advisory only in Step 11U: even SafeToMarkFailed never mutates durable state.
public enum ExecutionReconciliationDisposition
{
    NeedsManualReview, SafeToMarkFailed, AlreadyResolved, Unavailable
}

// Bounded source attestation. Its explicit bindings must match the state snapshot.
// A future adapter must read this from a trusted durable source, never chat or AI text.
public sealed record ExecutionReconciliationSnapshot(ActionExecutionState State,
    ActionIdentifier ActionId, ActionScope Scope, ToolIdentifier ToolId, Guid ApprovalId,
    Guid ExecutionId, long Version, ExecutionReconciliationReason Reason);

public interface IExecutionReconciliationSource
{
    Task<ExecutionReconciliationSnapshot?> GetAsync(ActionScope scope, ActionIdentifier actionId,
        CancellationToken cancellationToken);
}

public sealed class UnavailableExecutionReconciliationSource : IExecutionReconciliationSource
{
    public Task<ExecutionReconciliationSnapshot?> GetAsync(ActionScope scope, ActionIdentifier actionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ExecutionReconciliationSnapshot?>(null);
    }
}

public interface IExecutionReconciliationScopeValidator
{
    // Future adapter: authenticate current user, owned conversation, exact project
    // association, and owned project. IDs or approval alone prove no ownership.
    Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken);
}

public sealed class DenyExecutionReconciliationScopeValidator : IExecutionReconciliationScopeValidator
{
    public Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}

public interface IExecutionReconciliationPolicy
{
    Task<ExecutionReconciliationDisposition> AssessAsync(ExecutionReconciliationSnapshot snapshot,
        CancellationToken cancellationToken);
}

public sealed class ConservativeExecutionReconciliationPolicy : IExecutionReconciliationPolicy
{
    public Task<ExecutionReconciliationDisposition> AssessAsync(ExecutionReconciliationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExecutionReconciliationDisposition.NeedsManualReview);
    }
}

public sealed class ExecutionReconciliationIdentifier
{
    public Guid Value { get; }
    private ExecutionReconciliationIdentifier(Guid value) => Value = value;
    internal static ExecutionReconciliationIdentifier Create() => new(Guid.NewGuid());
}

public sealed class ExecutionReconciliationEvidence
{
    public ExecutionReconciliationIdentifier Id { get; }
    public ActionIdentifier ActionId { get; }
    public ActionScope Scope { get; }
    public ToolIdentifier ToolId { get; }
    public Guid ApprovalId { get; }
    public Guid ExecutionId { get; }
    public ActionExecutionStateStatus ObservedStatus { get; }
    public long ObservedVersion { get; }
    public ExecutionReconciliationReason Reason { get; }
    public ExecutionReconciliationDisposition Disposition { get; }
    public DateTime IssuedAt { get; }

    private ExecutionReconciliationEvidence(ExecutionReconciliationSnapshot snapshot,
        ExecutionReconciliationDisposition disposition, DateTime issuedAt)
    {
        Id = ExecutionReconciliationIdentifier.Create(); ActionId = snapshot.ActionId;
        Scope = snapshot.Scope; ToolId = snapshot.ToolId; ApprovalId = snapshot.ApprovalId;
        ExecutionId = snapshot.ExecutionId; ObservedStatus = snapshot.State.Status;
        ObservedVersion = snapshot.Version; Reason = snapshot.Reason;
        Disposition = disposition; IssuedAt = issuedAt;
    }

    internal static ExecutionReconciliationEvidence Issue(ExecutionReconciliationSnapshot snapshot,
        ExecutionReconciliationDisposition disposition, DateTime issuedAt)
    {
        if (snapshot is null || snapshot.State?.Status != ActionExecutionStateStatus.Reserved ||
            !Enum.IsDefined(snapshot.Reason) ||
            disposition is not (ExecutionReconciliationDisposition.NeedsManualReview or
                ExecutionReconciliationDisposition.SafeToMarkFailed) ||
            issuedAt.Kind != DateTimeKind.Utc || issuedAt == default)
            throw new InvalidOperationException("Invalid reconciliation assessment.");
        return new(snapshot, disposition, issuedAt);
    }
}

public enum ExecutionReconciliationStatus
{
    Success, InvalidRequest, ScopeDenied, NotEligible, AlreadyResolved,
    StateUnavailable, StateConflict, PolicyDenied, Failed
}

public sealed class ExecutionReconciliationResult
{
    public ExecutionReconciliationStatus Status { get; }
    public string Message { get; }
    public ExecutionReconciliationEvidence? Evidence { get; }
    private ExecutionReconciliationResult(ExecutionReconciliationStatus status, string message,
        ExecutionReconciliationEvidence? evidence = null)
    { Status = status; Message = message; Evidence = evidence; }

    internal static ExecutionReconciliationResult Success(ExecutionReconciliationEvidence evidence) =>
        new(ExecutionReconciliationStatus.Success, "Reconciliation assessment recorded.", evidence);

    public static ExecutionReconciliationResult Denied(ExecutionReconciliationStatus status) => status switch
    {
        ExecutionReconciliationStatus.InvalidRequest => new(status, "Invalid reconciliation request."),
        ExecutionReconciliationStatus.ScopeDenied => new(status, "Reconciliation scope is unavailable."),
        ExecutionReconciliationStatus.NotEligible => new(status, "Execution state is not eligible."),
        ExecutionReconciliationStatus.AlreadyResolved => new(status, "Execution state is already resolved."),
        ExecutionReconciliationStatus.StateUnavailable => new(status, "Execution state is unavailable."),
        ExecutionReconciliationStatus.StateConflict => new(status, "Execution state binding conflicts."),
        ExecutionReconciliationStatus.PolicyDenied => new(status, "Reconciliation policy denied."),
        _ => new(ExecutionReconciliationStatus.Failed, "Reconciliation assessment failed.")
    };
}

public interface IExecutionReconciliationService
{
    Task<ExecutionReconciliationResult> ReconcileAsync(ActionScope scope,
        ExecutionReconciliationRequest request, CancellationToken cancellationToken = default);
}
