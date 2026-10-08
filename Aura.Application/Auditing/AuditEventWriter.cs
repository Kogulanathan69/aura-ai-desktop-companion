using Aura.Application.Common.Interfaces;

namespace Aura.Application.Auditing;

public interface IAuditEventWriter
{
    Task<AuditWriteResult> WriteAsync(AuditWriteRequest request,
        CancellationToken cancellationToken = default);
}

// Default workflow boundary: no sink, no persistence claim.
public sealed class UnavailableAuditEventWriter : IAuditEventWriter
{
    public Task<AuditWriteResult> WriteAsync(AuditWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AuditWriteResult.Denied(AuditWriteStatus.Unavailable));
    }
}

public sealed class AuditEventWriter(IAuditEventSink sink, IDateTimeProvider clock) : IAuditEventWriter
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

internal static class AuditObservation
{
    internal static async Task RecordAsync(IAuditEventWriter writer, AuditWriteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = await writer.WriteAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* Audit is observational; no retry or business-result change. */ }
    }
}
