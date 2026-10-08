using Aura.Application.Common.Interfaces;

namespace Aura.Application.Auditing;

// Explicit boundary only. No action/approval/execution/verification/reconciliation
// workflow calls this writer automatically in Step 11W.
public sealed class AuditEventWriter(IAuditEventSink sink, IDateTimeProvider clock)
{
    public async Task<AuditWriteResult> WriteAsync(AuditWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var validation = AuditEventValidation.Evaluate(request);
            if (validation != AuditWriteStatus.Success) return AuditWriteResult.Denied(validation);
            var occurredAt = clock.UtcNow;
            if (occurredAt.Kind != DateTimeKind.Utc || occurredAt == default)
                return AuditWriteResult.Denied(AuditWriteStatus.InvalidEvent);
            var record = AuditEventRecord.Issue(request, occurredAt);
            var status = await sink.WriteAsync(record, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return status switch
            {
                AuditSinkStatus.Accepted => AuditWriteResult.Accepted(record),
                AuditSinkStatus.Unavailable => AuditWriteResult.Denied(AuditWriteStatus.Unavailable),
                _ => AuditWriteResult.Denied(AuditWriteStatus.Failed)
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return AuditWriteResult.Denied(AuditWriteStatus.Failed); }
    }
}
