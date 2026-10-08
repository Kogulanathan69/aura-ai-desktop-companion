using Aura.Application.Actions;
using Aura.Application.Tools;

namespace Aura.Application.Auditing;

public enum AuditEventType
{
    ActionProposed, ApprovalRequested, ApprovalGranted, ApprovalRejected,
    PermissionAllowed, PermissionDenied, ExecutionReserved, ExecutionCompleted,
    ExecutionFailed, ExecutionReconciliationAssessed, VerificationAssessed,
    SecurityDenied
}

public enum AuditEventOutcome { Success, Denied, Failed, Inconclusive }

// No arbitrary entity names, descriptions, values, messages, or metadata.
public sealed record AuditEventReferences(ActionIdentifier? ActionId = null,
    ToolIdentifier? ToolId = null, Guid? ApprovalId = null, Guid? ExecutionId = null,
    Guid? VerificationId = null, Guid? ReconciliationId = null,
    Guid? PermissionGrantId = null);

// No supported external construction. Future trusted workflow integration may use
// narrow internal factories; the shared internal constructor is validated before use.
public sealed class AuditWriteRequest
{
    public AuditEventType EventType { get; }
    public AuditEventOutcome Outcome { get; }
    public ActionScope Scope { get; }
    public AuditEventReferences References { get; }
    private AuditWriteRequest(AuditEventType eventType, AuditEventOutcome outcome,
        ActionScope scope, AuditEventReferences references)
    { EventType = eventType; Outcome = outcome; Scope = scope; References = references; }

    internal static AuditWriteRequest Create(AuditEventType eventType, AuditEventOutcome outcome,
        ActionScope scope, AuditEventReferences references) =>
        new(eventType, outcome, scope, references);
}

public sealed class AuditEventIdentifier
{
    public Guid Value { get; }
    private AuditEventIdentifier(Guid value) => Value = value;
    internal static AuditEventIdentifier Create() => new(Guid.NewGuid());
}

public sealed class AuditEventRecord
{
    public AuditEventIdentifier Id { get; }
    public AuditEventType EventType { get; }
    public AuditEventOutcome Outcome { get; }
    public ActionScope Scope { get; }
    public ActionIdentifier? ActionId { get; }
    public ToolIdentifier? ToolId { get; }
    public Guid? ApprovalId { get; }
    public Guid? ExecutionId { get; }
    public Guid? VerificationId { get; }
    public Guid? ReconciliationId { get; }
    public Guid? PermissionGrantId { get; }
    public DateTime OccurredAt { get; }

    private AuditEventRecord(AuditWriteRequest request, DateTime occurredAt)
    {
        Id = AuditEventIdentifier.Create(); EventType = request.EventType;
        Outcome = request.Outcome; Scope = request.Scope;
        ActionId = request.References.ActionId; ToolId = request.References.ToolId;
        ApprovalId = request.References.ApprovalId; ExecutionId = request.References.ExecutionId;
        VerificationId = request.References.VerificationId;
        ReconciliationId = request.References.ReconciliationId;
        PermissionGrantId = request.References.PermissionGrantId; OccurredAt = occurredAt;
    }

    internal static AuditEventRecord Issue(AuditWriteRequest request, DateTime occurredAt)
    {
        if (AuditEventValidation.Evaluate(request) != AuditWriteStatus.Success ||
            occurredAt.Kind != DateTimeKind.Utc || occurredAt == default)
            throw new InvalidOperationException("Invalid audit event.");
        return new(request, occurredAt);
    }
}

public enum AuditWriteStatus
{
    Success, InvalidEvent, InvalidScope, InvalidBinding, Unavailable, Failed
}

public sealed class AuditWriteResult
{
    public AuditWriteStatus Status { get; }
    public string Message { get; }
    public AuditEventRecord? Record { get; }
    private AuditWriteResult(AuditWriteStatus status, string message, AuditEventRecord? record = null)
    { Status = status; Message = message; Record = record; }

    internal static AuditWriteResult Accepted(AuditEventRecord record) =>
        new(AuditWriteStatus.Success, "Audit event accepted by sink.", record);

    public static AuditWriteResult Denied(AuditWriteStatus status) => status switch
    {
        AuditWriteStatus.InvalidEvent => new(status, "Invalid audit event."),
        AuditWriteStatus.InvalidScope => new(status, "Invalid audit scope."),
        AuditWriteStatus.InvalidBinding => new(status, "Invalid audit event binding."),
        AuditWriteStatus.Unavailable => new(status, "Audit sink is unavailable."),
        _ => new(AuditWriteStatus.Failed, "Audit event write failed.")
    };
}

public enum AuditSinkStatus { Accepted, Unavailable, Failed }

public interface IAuditEventSink
{
    Task<AuditSinkStatus> WriteAsync(AuditEventRecord record, CancellationToken cancellationToken);
}

public sealed class UnavailableAuditEventSink : IAuditEventSink
{
    public Task<AuditSinkStatus> WriteAsync(AuditEventRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AuditSinkStatus.Unavailable);
    }
}
