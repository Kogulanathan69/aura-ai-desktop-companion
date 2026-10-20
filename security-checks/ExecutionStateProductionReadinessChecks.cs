using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class ExecutionStateProductionReadinessChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        var store = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Services",
            "ActionExecutionStatePersistenceStore.cs"));
        var action = File.ReadAllText(Path.Combine(root, "Aura.Domain", "Entities", "AIAction.cs"));
        var approval = File.ReadAllText(Path.Combine(root, "Aura.Domain", "Entities", "ActionApproval.cs"));
        check(store.Contains("db.Users.AsNoTracking().AnyAsync") &&
            store.Contains("db.Projects.AsNoTracking().AnyAsync") &&
            store.Contains("db.Conversations.AsNoTracking().AnyAsync") &&
            store.Contains("db.AIActions.AsNoTracking().AnyAsync") &&
            store.Contains("db.ActionApprovals.AsNoTracking().AnyAsync") &&
            store.Contains("x.Decision == \"Approved\"") &&
            store.Contains("x.ExpiresAt == null || x.ExpiresAt > now"),
            "17X initial reservation validates one durable ownership/action/approval chain");
        check(!action.Contains("ToolId") && !approval.Contains("ToolId"),
            "17X persisted action and approval cannot yet prove exact ToolId binding");
        var services = new ServiceCollection();
        services.AddScoped<IActionExecutionStateStore, ActionExecutionStatePersistenceStore>();
        check(!ProductionRuntimeCompositionInspector.Inspect(services).ExecutionState,
            "17X state store alone remains false without factory and changed-path evidence");
        return Task.CompletedTask;
    }
}
