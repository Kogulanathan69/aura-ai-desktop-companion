using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Approvals;
using Aura.Application.Auditing;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionTrustedExecutionEvidenceChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var contract = typeof(ITrustedToolExecutionService).GetMethod(nameof(ITrustedToolExecutionService.ExecuteAsync));
        check(typeof(ITrustedToolExecutionService).GetMethods().Length == 1 &&
            contract?.ReturnType == typeof(Task<ExecutionOperationResult>) &&
            contract.GetParameters().Select(p => p.ParameterType).SequenceEqual(
                [typeof(ActionScope), typeof(ActionDescriptor), typeof(ApprovalEvidence),
                    typeof(ExecutionRequest), typeof(CancellationToken)]),
            "15J trusted execution contract has exact scope/action/approval/request/cancellation inputs");
        var implementations = typeof(TrustedToolExecutionService).Assembly.GetTypes()
            .Concat(typeof(AuraDbContext).Assembly.GetTypes())
            .Concat(typeof(ProductionRuntimeCompositionInspector).Assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITrustedToolExecutionService).IsAssignableFrom(t))
            .ToArray();
        check(implementations.SequenceEqual([typeof(TrustedToolExecutionService)]) &&
            typeof(TrustedToolExecutionService).IsSealed,
            "15J sole production implementation is concrete sealed TrustedToolExecutionService");
        var dependencies = typeof(TrustedToolExecutionService).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).ToArray();
        check(dependencies.SequenceEqual([typeof(ToolRegistry), typeof(IToolExecutionScopeValidator),
            typeof(IToolExecutionPermissionValidator), typeof(IToolExecutionPolicy),
            typeof(IDateTimeProvider), typeof(IActionExecutionStateStore), typeof(IAuditEventWriter)]),
            "15J trusted executor dependencies are explicit; audit writer is optional");
        check(typeof(ExecutionRequest).GetProperties().Select(p => p.Name).SequenceEqual(["ActionId"]) &&
            typeof(ExecutionOperationResult).GetConstructors().Length == 0 &&
            typeof(ExecutionOperationResult).GetProperties().Select(p => p.Name)
                .SequenceEqual(["Status", "Message", "Action", "Evidence"]) &&
            typeof(ExecutionOperationResult).GetProperties().All(p => p.SetMethod is null) &&
            Enum.GetValues<ExecutionOperationStatus>().All(s =>
                ExecutionOperationResult.Denied(s).Message.Length <= ExecutionLimits.MaximumResultMessageCharacters),
            "15J request cannot carry command/path/URL/bypass; denied results have fixed bounded messages");

        var root = Directory.GetCurrentDirectory();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var service = Source("Aura.Application", "Executions", "TrustedToolExecutionService.cs");
        var audit = Source("Aura.Application", "Auditing", "AuditEventWriter.cs");
        var store = Source("Aura.Infrastructure", "Services", "ActionExecutionStatePersistenceStore.cs");
        var reconciliation = Source("Aura.Application", "Executions", "ExecutionReconciliationService.cs");
        var verification = Source("Aura.Application", "Verifications", "VerificationWorkflowService.cs");
        var api = Source("Aura.Api", "Program.cs");
        var inspector = Source("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        var entry = Source("security-checks", "Program.cs");
        var integration = Source("security-checks", "ExecutionIntegrationChecks.cs");
        var executionChecks = Source("security-checks", "ExecutionChecks.cs");
        var auditChecks = Source("security-checks", "AuditWorkflowChecks.cs");

        int At(string marker) => service.IndexOf(marker, StringComparison.Ordinal);
        var sequence = new[] {
            "if (action.Scope != scope)",
            "ApprovalValidation.IsBoundToApprovedAction",
            "scopes.ValidateAsync(scope, cancellationToken)",
            "tools.TryResolve(action.ToolId",
            "permissions.HasPermissionAsync(scope, tool, cancellationToken)",
            "policy.AllowsAsync(tool, cancellationToken)",
            "ActionExecutionState.CreateReady(action, approval)",
            "states.TryReserveInitialAsync(ready, attempt, cancellationToken)",
            "reservedState.Version != 1",
            "AuditEventType.ExecutionReserved",
            "handler.ExecuteAsync(new ToolInvocationRequest(action.ToolId), cancellationToken)",
            "states.TryCompleteAsync(new(action.Id, scope, action.ToolId",
            "AuditEventType.ExecutionCompleted"
        };
        check(sequence.All(s => At(s) >= 0) && sequence.Zip(sequence.Skip(1))
                .All(pair => At(pair.First) < At(pair.Second)) &&
            At("scopes.ValidateAsync(scope, cancellationToken)") <
                At("permissions.HasPermissionAsync(scope, tool, cancellationToken)") &&
            service.Split("handler.ExecuteAsync(").Length == 2,
            "15J actual order: action/approval, ownership, tool, permission, policy, reservation, audit observation, one handler, completion, post-audit");
        check(service.Contains("owned = await scopes.ValidateAsync(scope, cancellationToken);") &&
            service.Split("scopes.ValidateAsync(scope, cancellationToken)").Length == 3 &&
            service.Contains("if (!allowed) return ExecutionOperationResult.Denied(ExecutionOperationStatus.PolicyDenied)") &&
            service.Contains("if (!permitted) return ExecutionOperationResult.Denied(ExecutionOperationStatus.PermissionDenied)") &&
            service.Contains("ExecutionOperationStatus.ApprovalDenied") &&
            service.Contains("ExecutionOperationStatus.UnknownTool") &&
            service.Contains("if (reservation.Status != ActionExecutionStateResultStatus.Success)"),
            "15J ownership recheck and every pre-handler denial boundary remain explicit");
        check(service.Contains("reservedState.ActionId != action.Id") &&
            service.Contains("reservedState.Scope != scope") &&
            service.Contains("reservedState.ToolId != action.ToolId") &&
            service.Contains("reservedState.ApprovalId != approval.Id.Value") &&
            service.Contains("reservedState.ExecutionId != attempt.Value") &&
            service.Contains("reservedState.Version != 1") &&
            service.Contains("completedState.Version != reservedState.Version + 1") &&
            service.Contains("ExecutionOperationStatus.ReconciliationRequired") &&
            service.Contains("reserved || reservationAttempted"),
            "15J reservation/completion bindings and uncertain-result reconciliation category remain bounded");
        check(store.Contains("BeginTransactionAsync") &&
            store.Contains("AddPendingAuditOutbox(") &&
            store.IndexOf("AddPendingAuditOutbox(", StringComparison.Ordinal) <
                store.IndexOf("CommitAsync(", StringComparison.Ordinal) &&
            store.Contains("ActionExecutionStateResultStatus.Success"),
            "15J unregistered persistence store stages pending audit intent in reservation transaction");
        check(audit.Contains("class AuditObservation") &&
            audit.Contains("catch (Exception) { /* Audit is observational; no retry or business-result change. */ }") &&
            service.Contains("new UnavailableAuditEventWriter()") &&
            service.Contains("AuditEventType.ExecutionFailed") &&
            service.Contains("catch (Exception) { handlerResult = ToolExecutionResult.FromStatus(ToolExecutionStatus.Failed); }") &&
            service.Contains("catch (OperationCanceledException) { throw; }") &&
            !service.Contains("Task.Delay") && !service.Contains("while (") &&
            !service.Contains("HttpClient") && !service.Contains("Process.Start") &&
            !service.Contains("File.") && !service.Contains("MigrateAsync") &&
            !service.Contains("EnsureCreated"),
            "15J audit is observational; handler failures are fixed, cancellation propagates, no retry/provider/file/process/migration path");
        check(service.Contains("ToVerificationInput") == false &&
            service.Contains("!ActionExecutionStatePolicy.IsValid(completedState)") &&
            reconciliation.Contains("Assessment only: never calls a tool") &&
            !reconciliation.Contains("TryCompleteAsync") &&
            verification.Contains("IExecutionEvidenceSource"),
            "15J execution report is not verification; reconciliation assessment does not complete state");

        // Existing behavior fixtures run in the same offline suite; pin the scenarios
        // that prove denial, cancellation, audit failure, single invocation and uncertainty.
        check(entry.Contains("await ExecutionChecks.RunAsync(Check)") &&
            entry.Contains("await ProductionTrustedExecutionEvidenceChecks.RunAsync(Check)") &&
            integration.Contains("valid.Handler.Calls == 1") &&
            integration.Contains("scenario.Handler.Calls == 0") &&
            integration.Contains("scenario.Handler.Calls == 1 && scenario.Store.Completions == 0") &&
            integration.Contains("duringHandler.Handler.Calls == 1") &&
            integration.Contains("afterReserve.Handler.Calls == 0") &&
            executionChecks.Contains("ExecutionOperationStatus.ScopeDenied") &&
            executionChecks.Contains("ExecutionOperationStatus.PermissionDenied") &&
            executionChecks.Contains("ExecutionOperationStatus.PolicyDenied") &&
            executionChecks.Contains("ExecutionOperationStatus.ApprovalDenied") &&
            executionChecks.Contains("ExecutionOperationStatus.UnknownTool") &&
            auditChecks.Contains("sink.ThrowFailure = true") &&
            auditChecks.Contains("handler.Calls == beforeHandler + 1"),
            "15J existing runtime fixtures cover pre-handler gates, one call, failure, cancellation, audit and uncertain completion");

        var catalog = typeof(ProductionRuntimeCompositionInspector).GetField("ReviewedTrustedExecution",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as IReadOnlySet<Type>;
        var exact = new ServiceCollection();
        exact.AddScoped<ITrustedToolExecutionService, TrustedToolExecutionService>();
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(exact);
        check(catalog is { Count: 0 } && !snapshot.TrustedExecution && !snapshot.CompositionComplete &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().ExecutionState,
            "15J exact scoped synthetic registration cannot overcome empty catalog/prerequisites");
        var gate = typeof(ProductionRuntimeCompositionInspector).GetMethod("TrustedPrerequisitesReady",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Ready(bool[] values) => gate.Invoke(null, values.Cast<object>().ToArray()) is true;
        var all = Enumerable.Repeat(true, 9).ToArray();
        check(Ready(all), "15J prerequisite predicate requires all nine true");
        for (var i = 0; i < 9; i++)
        {
            var values = (bool[])all.Clone();
            values[i] = false;
            check(!Ready(values), "15J prerequisite " + i + " false blocks trusted execution");
        }
        check(inspector.Contains("HasExactTrustedExecutionService(services) &&") &&
            inspector.Contains("TrustedPrerequisitesReady(ownership, permission, executionState, auditSink,") &&
            inspector.Contains("ImplementationType == typeof(TrustedToolExecutionService)") &&
            inspector.Contains("ReviewedTrustedExecution.Contains(typeof(TrustedToolExecutionService))") &&
            inspector.Contains("registrations.Length == 1") &&
            inspector.Contains("ServiceLifetime.Scoped") &&
            !api.Contains("AddScoped<ITrustedToolExecutionService") &&
            !api.Contains("AddSingleton<ITrustedToolExecutionService") &&
            !api.Contains("AddTransient<ITrustedToolExecutionService") &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()") &&
            !api.Contains("AddHostedService") && !api.Contains("MigrateAsync(") &&
            !api.Contains("EnsureCreated("),
            "15J production requires one exact scoped reviewed service plus nine fields; current startup has none and no worker/migration");
        var flags = new RuntimeSafetyOptions { ProductionAdaptersEnabled = true,
            ToolExecutionEnabled = true, AuditPersistenceEnabled = true };
        check(flags.ToolExecutionEnabled &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).TrustedExecution &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).CompositionComplete,
            "15J flags cannot make trusted execution or Step 14P composition ready");
        return Task.CompletedTask;
    }
}
