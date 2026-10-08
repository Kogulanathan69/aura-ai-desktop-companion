using Aura.Application.Executions;

// One-state, lock-protected security-check fake only. No production persistence claim.
internal sealed class AtomicExecutionStore : IActionExecutionStateStore
{
    private readonly object gate = new();
    public ActionExecutionState? State { get; private set; }
    public int Reservations { get; private set; }
    public int Completions { get; private set; }
    public CompleteExecutionRequest? LastCompletionRequest { get; private set; }
    public ActionExecutionStateResultStatus? ReserveOverride { get; set; }
    public ActionExecutionStateResultStatus? CompleteOverride { get; set; }
    public bool ThrowOnReserve { get; set; }
    public bool ThrowOnComplete { get; set; }
    public CancellationTokenSource? CancelAfterReserve { get; set; }
    public CancellationTokenSource? CancelOnComplete { get; set; }

    public Task<ActionExecutionStateResult> TryReserveInitialAsync(ActionExecutionState ready,
        ExecutionAttemptIdentifier executionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnReserve) throw new InvalidOperationException("raw secret store failure");
            if (ReserveOverride is { } status) return Task.FromResult(ActionExecutionStateResult.Denied(status));
            if (!ActionExecutionStatePolicy.IsValid(ready) || ready.Status != ActionExecutionStateStatus.Ready ||
                ready.Version != 0)
                return Task.FromResult(ActionExecutionStateResult.Denied(ActionExecutionStateResultStatus.InvalidState));
            State ??= ready;
            var request = new ReserveExecutionRequest(ready.ActionId, ready.Scope, ready.ToolId,
                ready.ApprovalId, 0, executionId);
            var result = ActionExecutionStatePolicy.TryReserve(State, request);
            if (result.State is not null) { State = result.State; Reservations++; }
            CancelAfterReserve?.Cancel();
            return Task.FromResult(result);
        }
    }

    public Task<ActionExecutionStateResult> TryReserveAsync(ReserveExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ActionExecutionStatePolicy.TryReserve(State, request);
            if (result.State is not null) { State = result.State; Reservations++; }
            return Task.FromResult(result);
        }
    }

    public Task<ActionExecutionStateResult> TryCompleteAsync(CompleteExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnComplete) throw new InvalidOperationException("raw secret completion failure");
            LastCompletionRequest = request;
            CancelOnComplete?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (CompleteOverride is { } status) return Task.FromResult(ActionExecutionStateResult.Denied(status));
            var result = ActionExecutionStatePolicy.TryComplete(State, request);
            if (result.State is not null) { State = result.State; Completions++; }
            return Task.FromResult(result);
        }
    }
}
