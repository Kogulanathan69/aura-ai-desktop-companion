using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Security;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

internal static class SecurityAuditOutboxDispatcherChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var auditId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;

        var restored =
            AuditEventRecord.RestoreExecutionOutbox(
                auditId,
                AuditEventType.ExecutionReserved,
                AuditEventOutcome.Success,
                occurredAt,
                userId,
                conversationId,
                projectId,
                actionId,
                "exact-tool",
                approvalId,
                executionId);

        check(
            restored is not null &&
            restored.Id.Value == auditId &&
            restored.EventType == AuditEventType.ExecutionReserved &&
            restored.Outcome == AuditEventOutcome.Success &&
            restored.OccurredAt == occurredAt &&
            restored.Scope.UserId == userId &&
            restored.Scope.ConversationId == conversationId &&
            restored.Scope.ProjectId == projectId &&
            restored.ActionId?.Value == actionId &&
            restored.ToolId?.Value == "exact-tool" &&
            restored.ApprovalId == approvalId &&
            restored.ExecutionId == executionId &&
            restored.VerificationId is null &&
            restored.ReconciliationId is null &&
            restored.PermissionGrantId is null,
            "13H restore preserves stable event ID and every exact bounded execution binding");

        check(
            AuditEventRecord.RestoreExecutionOutbox(
                Guid.Empty,
                AuditEventType.ExecutionReserved,
                AuditEventOutcome.Success,
                occurredAt,
                userId,
                conversationId,
                projectId,
                actionId,
                "exact-tool",
                approvalId,
                executionId) is null &&

            AuditEventRecord.RestoreExecutionOutbox(
                auditId,
                AuditEventType.ExecutionReserved,
                AuditEventOutcome.Failed,
                occurredAt,
                userId,
                conversationId,
                projectId,
                actionId,
                "exact-tool",
                approvalId,
                executionId) is null &&

            AuditEventRecord.RestoreExecutionOutbox(
                auditId,
                AuditEventType.SecurityDenied,
                AuditEventOutcome.Denied,
                occurredAt,
                userId,
                conversationId,
                projectId,
                actionId,
                "exact-tool",
                approvalId,
                executionId) is null &&

            AuditEventRecord.RestoreExecutionOutbox(
                auditId,
                AuditEventType.ExecutionReserved,
                AuditEventOutcome.Success,
                occurredAt,
                userId,
                conversationId,
                projectId,
                actionId,
                "bad*tool",
                approvalId,
                executionId) is null,
            "13H malformed event, outcome and tool cannot be restored");

        check(
            AuditEventRecord.RestoreExecutionOutbox(
                auditId,
                AuditEventType.ExecutionCompleted,
                AuditEventOutcome.Success,
                occurredAt,
                userId,
                conversationId,
                projectId,
                actionId,
                "exact-tool",
                approvalId,
                executionId) is not null &&

            AuditEventRecord.RestoreExecutionOutbox(
                auditId,
                AuditEventType.ExecutionFailed,
                AuditEventOutcome.Failed,
                occurredAt,
                userId,
                conversationId,
                projectId,
                actionId,
                "exact-tool",
                approvalId,
                executionId) is not null,
            "13H completion and failure restore only reviewed event families");

        check(
            typeof(IAuditOutboxDispatcher)
                .IsAssignableFrom(typeof(SecurityAuditOutboxDispatcher)) &&
            Enum.GetValues<AuditOutboxDispatchStatus>().Length == 7,
            "13H bounded single-item dispatcher contract and implementation exist");

        var dispatcher =
            new SecurityAuditOutboxDispatcher(
                null!,
                null!);

        check(
            (await dispatcher.DispatchAsync(
                Guid.Empty,
                CancellationToken.None))
                .Status ==
            AuditOutboxDispatchStatus.InvalidRequest,
            "13H empty outbox ID fails before database or sink access");

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();

            var propagated = false;

            try
            {
                await dispatcher.DispatchAsync(
                    Guid.NewGuid(),
                    cancelled.Token);
            }
            catch (OperationCanceledException)
            {
                propagated = true;
            }

            check(
                propagated,
                "13H cancellation propagates before database or sink access");
        }

        var root =
            Directory.GetCurrentDirectory();

        var source =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "Aura.Infrastructure",
                    "Services",
                    "SecurityAuditOutboxDispatcher.cs"));

        var sink =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "Aura.Infrastructure",
                    "Services",
                    "SecurityAuditPersistenceSink.cs"));

        var api =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "Aura.Api",
                    "Program.cs"));

        var compactSource =
            string.Concat(
                source.Where(c =>
                    !char.IsWhiteSpace(c)));

        var compactSink =
            string.Concat(
                sink.Where(c =>
                    !char.IsWhiteSpace(c)));

        check(
            compactSource.Contains(
                "x.Id==outboxId",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "SingleOrDefaultAsync",
                StringComparison.Ordinal) &&
            !compactSource.Contains(
                "ToListAsync",
                StringComparison.Ordinal) &&
            !compactSource.Contains(
                "while(",
                StringComparison.Ordinal) &&
            !compactSource.Contains(
                "Task.Delay",
                StringComparison.Ordinal),
            "13H dispatcher accepts exact ID and has no table scan or retry loop");

        check(
            compactSource.Contains(
                "x.DeliveryState==SecurityAuditOutboxDeliveryState.Pending",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "x.AttemptCount==0",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "x.DeliveredAt==null",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "x=>x.AttemptCount,x=>x.AttemptCount",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "BeginTransactionAsync(cancellationToken)",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "ExecuteUpdateAsync",
                StringComparison.Ordinal),
            "13H exact Pending conditional row lock and CAS guard terminal transition");

        check(
            compactSource.Contains(
                "AuditEventRecord.RestoreExecutionOutbox(row.AuditEventId",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "sink.WriteAsync(audit!,cancellationToken)",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "sinkStatus==AuditSinkStatus.Accepted",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                ".SetProperty(x=>x.AttemptCount,1)",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                ".SetProperty(x=>x.DeliveredAt,deliveredAt)",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "DateTime?deliveredAt=delivered?DateTime.UtcNow:null",
                StringComparison.Ordinal),
            "13H stable event delivery increments one attempt and timestamps Delivered only");

        // Unavailable is transient.
        // It must return before terminal Delivered/Failed mutation.
        check(
            compactSource.Contains(
                "if(sinkStatus==AuditSinkStatus.Unavailable)",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "returnResult(AuditOutboxDispatchStatus.Unavailable);",
                StringComparison.Ordinal) &&
            compactSource.IndexOf(
                "if(sinkStatus==AuditSinkStatus.Unavailable)",
                StringComparison.Ordinal) <
            compactSource.IndexOf(
                "vardelivered=sinkStatus==AuditSinkStatus.Accepted",
                StringComparison.Ordinal),
            "13H unavailable sink returns before terminal outbox mutation");

        check(
            compactSource.Contains(
                "SecurityAuditOutboxDeliveryState.Delivered",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                "SecurityAuditOutboxDeliveryState.Failed",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                ".SetProperty(x=>x.AttemptCount,1)",
                StringComparison.Ordinal) &&
            compactSource.Contains(
                ".SetProperty(x=>x.DeliveredAt,deliveredAt)",
                StringComparison.Ordinal),
            "13H only explicit terminal delivery result consumes one attempt");

        check(
            compactSink.Contains(
                "ResolveDuplicateAsync",
                StringComparison.Ordinal) &&
            compactSink.Contains(
                "x.Id==record.Id.Value",
                StringComparison.Ordinal) &&
            compactSink.Contains(
                "existing.EventType==(int)record.EventType",
                StringComparison.Ordinal) &&
            compactSink.Contains(
                "existing.Outcome==(int)record.Outcome",
                StringComparison.Ordinal) &&
            new[]
            {
                "UserId",
                "ConversationId",
                "ProjectId"
            }.All(name =>
                compactSink.Contains(
                    $"existing.{name}==record.Scope.{name}",
                    StringComparison.Ordinal)) &&
            new[]
            {
                "ApprovalId",
                "ExecutionId",
                "VerificationId",
                "ReconciliationId",
                "PermissionGrantId"
            }.All(name =>
                compactSink.Contains(
                    $"existing.{name}==record.{name}",
                    StringComparison.Ordinal)) &&
            compactSink.Contains(
                "existing.ActionId==record.ActionId?.Value",
                StringComparison.Ordinal) &&
            compactSink.Contains(
                "existing.ToolId==record.ToolId?.Value",
                StringComparison.Ordinal) &&
            compactSink.Contains(
                "existing.OccurredAt.Ticks==record.OccurredAt.Ticks-",
                StringComparison.Ordinal),
            "13H exact duplicate database event is accepted idempotently; mismatched event is rejected");

        check(
            !compactSource.Contains(
                "AddHostedService",
                StringComparison.Ordinal) &&
            !compactSource.Contains(
                "BackgroundService",
                StringComparison.Ordinal) &&
            !compactSource.Contains(
                "Timer",
                StringComparison.Ordinal) &&
            !api.Contains(
                "SecurityAuditOutboxDispatcher",
                StringComparison.Ordinal) &&
            !api.Contains(
                "IAuditOutboxDispatcher",
                StringComparison.Ordinal) &&
            !api.Contains(
                "AddHostedService",
                StringComparison.Ordinal),
            "13H no hosted worker, timer, polling, or API registration");

        check(
            !api.Contains(
                "MigrateAsync(",
                StringComparison.Ordinal) &&
            !api.Contains(
                "Database.Migrate(",
                StringComparison.Ordinal) &&
            !compactSource.Contains(
                "MigrateAsync(",
                StringComparison.Ordinal),
            "13H no automatic migration application");

        var options =
            new DbContextOptionsBuilder<AuraDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=metadata_only",
                    npgsql => npgsql.UseVector())
                .Options;

        await using var db =
            new AuraDbContext(options);

        var outbox =
            db.Model.FindEntityType(
                typeof(SecurityAuditOutboxRecord))!;

        check(
            outbox.GetProperties()
                .All(property =>
                    property.Name is not
                    ("Description" or
                     "Json" or
                     "Metadata" or
                     "Prompt" or
                     "FileContent")),
            "13H no arbitrary text, JSON, prompt or file-content payload field");

        var startup =
            new UnavailableRuntimeCapabilityReadiness()
                .GetSnapshot();

        var informational =
            new PersistenceCapabilityReadiness()
                .GetSnapshot();

        var runtime =
            new RuntimeSafetyOptions();

        check(
            !startup.AuditSink &&
            !startup.ExecutionState &&
            !informational.AuditSink &&
            !informational.ExecutionState &&
            !runtime.ProductionAdaptersEnabled &&
            !runtime.ToolExecutionEnabled &&
            !runtime.SafeFileAccessEnabled,
            "13H dispatcher presence does not enable runtime readiness or execution");
    }
}