using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

internal static class ExecutionStatePersistenceReadinessChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector())
            .Options;
        await using var db = new AuraDbContext(options);
        var execution = db.Model.FindEntityType(typeof(ToolExecution))!;
        var action = db.Model.FindEntityType(typeof(AIAction))!;
        var approval = db.Model.FindEntityType(typeof(ActionApproval))!;
        var verification = db.Model.FindEntityType(typeof(Verification))!;
        static HashSet<string> Fields(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity) =>
            entity.GetProperties().Select(property => property.Name).ToHashSet();
        var executionFields = Fields(execution);
        var actionFields = Fields(action);
        var approvalFields = Fields(approval);
        var verificationFields = Fields(verification);

        check(execution.GetTableName() == "ToolExecutions" &&
            executionFields.Contains(nameof(ToolExecution.Id)) &&
            executionFields.Contains(nameof(ToolExecution.AIActionId)) &&
            executionFields.Contains(nameof(ToolExecution.ToolName)) &&
            executionFields.Contains(nameof(ToolExecution.Status)) &&
            !executionFields.Contains("ApprovalId") && !executionFields.Contains("Version") &&
            !executionFields.Contains("ExecutionAttemptId") &&
            !executionFields.Contains("ApprovalConsumedByExecutionId") &&
            !executionFields.Contains("Outcome"),
            "12H ToolExecution has an action link but no exact durable state or CAS fields");
        check(action.GetTableName() == "AIActions" &&
            actionFields.Contains(nameof(AIAction.Id)) &&
            actionFields.Contains(nameof(AIAction.UserId)) &&
            actionFields.Contains(nameof(AIAction.ConversationId)) &&
            actionFields.Contains(nameof(AIAction.ProjectId)) &&
            !actionFields.Contains("ToolId") && !actionFields.Contains("ApprovalId") &&
            !actionFields.Contains("Version") &&
            action.FindProperty(nameof(AIAction.ConversationId))!.IsNullable &&
            action.FindProperty(nameof(AIAction.ProjectId))!.IsNullable,
            "12H AIAction scope links are optional and do not encode exact tool, approval or version");
        check(approval.GetTableName() == "ActionApprovals" &&
            approvalFields.Contains(nameof(ActionApproval.Id)) &&
            approvalFields.Contains(nameof(ActionApproval.AIActionId)) &&
            !approvalFields.Contains("ConsumedByExecutionId") &&
            !approvalFields.Contains("Version") &&
            !approval.GetIndexes().Any(index => index.IsUnique &&
                index.Properties.Any(property => property.Name == nameof(ActionApproval.Id)) &&
                index.Properties.Count > 1),
            "12H ActionApproval decision is not atomic consumption evidence");
        check(verification.GetTableName() == "Verifications" &&
            verificationFields.Contains(nameof(Verification.AIActionId)) &&
            verificationFields.Contains(nameof(Verification.ToolExecutionId)) &&
            !verificationFields.Contains("ExecutionAttemptId") &&
            !verificationFields.Contains("Version"),
            "12H Verification links do not provide reservation or completion CAS");
        check(new[] { execution, action, approval, verification }
            .All(entity => entity.GetProperties().All(property => !property.IsConcurrencyToken)),
            "12H reviewed execution entities have no EF concurrency token");
        check(!execution.GetIndexes().Any(index => index.IsUnique &&
                index.Properties.Any(property => property.Name == "ApprovalId")) &&
            !action.GetIndexes().Any(index => index.IsUnique &&
                index.Properties.Any(property => property.Name == "ApprovalId")),
            "12H no unique approval-consumption binding prevents cross-row replay");

        var stores = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IActionExecutionStateStore).Assembly.GetTypes())
            .Concat(typeof(RuntimeSafetyOptions).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract &&
                typeof(IActionExecutionStateStore).IsAssignableFrom(type)).ToArray();
        check(stores.SequenceEqual([typeof(UnavailableActionExecutionStateStore)]),
            "12H incompatible schema has no production durable-state adapter");
        var unavailable = new UnavailableActionExecutionStateStore();
        check((await unavailable.TryReserveInitialAsync(null!, null!, CancellationToken.None)).Status ==
                ActionExecutionStateResultStatus.Unavailable &&
            (await unavailable.TryReserveAsync(null!, CancellationToken.None)).Status ==
                ActionExecutionStateResultStatus.Unavailable &&
            (await unavailable.TryCompleteAsync(null!, CancellationToken.None)).Status ==
                ActionExecutionStateResultStatus.Unavailable,
            "12H initial reserve, reserve and completion remain unavailable without writes");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var propagated = false;
            try { await unavailable.TryReserveInitialAsync(null!, null!, cancelled.Token); }
            catch (OperationCanceledException) { propagated = true; }
            check(propagated, "12H unavailable state store propagates cancellation");
        }
        var readiness = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var defaults = new RuntimeSafetyOptions();
        check(!readiness.ExecutionState && !readiness.Ownership && !readiness.Permission &&
            !informational.ExecutionState && !defaults.ProductionAdaptersEnabled &&
            !defaults.ToolExecutionEnabled && !defaults.SafeFileAccessEnabled,
            "12H execution-state readiness and execution/file defaults remain disabled");
    }
}
