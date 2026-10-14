namespace Aura.Application.Auditing;

// Explicit single-item operation. No scan, scheduler, or implicit retry policy.
public enum AuditOutboxDispatchStatus
{
    Delivered, Failed, NotFound, NotPending, Conflict, Unavailable, InvalidRequest
}

public sealed record AuditOutboxDispatchResult(AuditOutboxDispatchStatus Status);

public interface IAuditOutboxDispatcher
{
    Task<AuditOutboxDispatchResult> DispatchAsync(Guid outboxId,
        CancellationToken cancellationToken);
}
