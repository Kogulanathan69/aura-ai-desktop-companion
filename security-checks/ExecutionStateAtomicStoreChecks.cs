using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Data.Migrations;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class ExecutionStateAtomicStoreChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector()).Options;
        await using var db = new AuraDbContext(options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ActionExecutionStateRecord))!;
        var fields = entity.GetProperties().Select(x => x.Name).ToHashSet();
        var constraints = entity.GetCheckConstraints().ToDictionary(x => x.Name, x => x.Sql);
        check(entity.GetTableName() == "ActionExecutionStates" &&
            entity.FindPrimaryKey()!.Properties.Single().Name == "ActionId" &&
            db.ActionExecutionStates is not null, "13C dedicated action-keyed durable table");
        foreach (var name in new[] { "ActionId", "UserId", "ConversationId", "ProjectId", "ApprovalId" })
            check(entity.FindProperty(name) is { IsNullable: false } &&
                constraints["CK_ActionExecutionStates_NonemptyIds"].Contains("\"" + name + "\" <>"),
                "13C exact nonempty required binding " + name);
        check(entity.FindProperty("ToolId") is { IsNullable: false } tool && tool.GetMaxLength() == 64 &&
            constraints["CK_ActionExecutionStates_ToolId"].Contains("^[a-z][a-z0-9-]{0,63}$"),
            "13C canonical bounded tool binding");
        check(entity.FindProperty("Status")!.ClrType == typeof(DurableExecutionStatus) &&
            entity.FindProperty("Status")!.GetColumnType() == "integer" &&
            entity.FindProperty("Version")!.ClrType == typeof(long) &&
            entity.FindProperty("ApprovalConsumed")!.ClrType == typeof(bool),
            "13C typed state, exact CAS version and explicit approval consumption");
        check(entity.FindProperty("ExecutionId")!.IsNullable &&
            !entity.FindProperty("CreatedAt")!.IsNullable &&
            entity.FindProperty("ReservedAt")!.IsNullable && entity.FindProperty("CompletedAt")!.IsNullable,
            "13C execution and timestamp nullability");
        check(fields.SetEquals(new[] { "ActionId", "UserId", "ConversationId", "ProjectId", "ToolId",
            "ApprovalId", "ExecutionId", "Status", "Version", "ApprovalConsumed", "CreatedAt",
            "ReservedAt", "CompletedAt" }), "13C no ambiguous execution fields");
        var state = constraints["CK_ActionExecutionStates_State"];
        check(state.Contains("\"Status\" = 0 AND \"Version\" = 0") &&
            state.Contains("\"Status\" = 1 AND \"Version\" = 1") &&
            state.Contains("\"Status\" IN (2, 3) AND \"Version\" = 2") &&
            state.Contains("NOT \"ApprovalConsumed\"") &&
            state.Contains("\"ExecutionId\" IS NOT NULL") &&
            state.Contains("\"CompletedAt\" IS NOT NULL"),
            "13C database restricts Ready, Reserved and terminal shape");
        check(entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Single().Name == "ApprovalId") &&
            entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Single().Name == "ExecutionId" &&
                x.GetFilter() == "\"ExecutionId\" IS NOT NULL"),
            "13C approval and nonnull execution replay indexes");
        foreach (var principal in new[] { typeof(AIAction), typeof(User), typeof(Conversation),
            typeof(Project), typeof(ActionApproval) })
            check(entity.GetForeignKeys().Any(x => x.PrincipalEntityType.ClrType == principal &&
                x.DeleteBehavior == DeleteBehavior.Restrict && x.Properties.Single().IsNullable == false),
                "13C required Restrict FK " + principal.Name);
        check(entity.GetForeignKeys().Count() == 5 && !entity.GetSeedData().Any(),
            "13C no optional authorization binding or seed data");

        var migration = new AddActionExecutionStates();
        check(migration.UpOperations.Count(x => x is CreateTableOperation) == 1 &&
            migration.UpOperations.All(x => x is CreateTableOperation or CreateIndexOperation) &&
            migration.DownOperations.Single() is DropTableOperation drop && drop.Name == "ActionExecutionStates",
            "13C compiled migration is additive and rollback only drops new table");
        var table = migration.UpOperations.OfType<CreateTableOperation>().Single();
        check(table.Columns.Select(x => x.Name).ToHashSet().SetEquals(fields) &&
            table.ForeignKeys.Count == 5 && table.CheckConstraints.Count == 3 &&
            table.ForeignKeys.All(x => x.OnDelete == Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict),
            "13C migration matches model and restricts all deletes");

        var root = Directory.GetCurrentDirectory();
        var program = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var store = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Services",
            "ActionExecutionStatePersistenceStore.cs"));
        check(!program.Contains("ActionExecutionStatePersistenceStore", StringComparison.Ordinal) &&
            !program.Contains("AddScoped<IActionExecutionStateStore", StringComparison.Ordinal),
            "13C production store remains unregistered");
        check(typeof(IActionExecutionStateStore).IsAssignableFrom(typeof(ActionExecutionStatePersistenceStore)) &&
            store.Contains("factory.CreateDbContextAsync(cancellationToken)", StringComparison.Ordinal) &&
            store.Contains("BeginTransactionAsync(cancellationToken)", StringComparison.Ordinal) &&
            store.Contains("ExecuteUpdateAsync", StringComparison.Ordinal) &&
            store.Contains("x.Version == request.ExpectedVersion", StringComparison.Ordinal),
            "13C store uses PostgreSQL transaction and conditional EF CAS");
        check(store.Contains("x.ApprovalId == request.ApprovalId", StringComparison.Ordinal) &&
            store.Contains("x.ExecutionId == request.ExecutionId.Value", StringComparison.Ordinal) &&
            store.Contains("x.UserId == request.Scope.UserId", StringComparison.Ordinal) &&
            store.Contains("x.ConversationId == request.Scope.ConversationId", StringComparison.Ordinal) &&
            store.Contains("x.ProjectId == request.Scope.ProjectId", StringComparison.Ordinal) &&
            store.Contains("x.ToolId == request.ToolId.Value", StringComparison.Ordinal),
            "13C both mutations include exact identity, scope, tool and approval predicates");
        check(store.Contains("x.Status == DurableExecutionStatus.Ready", StringComparison.Ordinal) &&
            store.Contains("x.Status == DurableExecutionStatus.Reserved", StringComparison.Ordinal) &&
            store.Contains(".SetProperty(x => x.Version, 1L)", StringComparison.Ordinal) &&
            store.Contains(".SetProperty(x => x.Version, 2L)", StringComparison.Ordinal) &&
            store.Contains(".SetProperty(x => x.ApprovalConsumed, true)", StringComparison.Ordinal),
            "13C store only advances Ready v0 to Reserved v1 to terminal v2");
        check(!store.Contains("ExecuteSql", StringComparison.Ordinal) &&
            !store.Contains("Thread.Sleep", StringComparison.Ordinal) &&
            !store.Contains("handler.Execute", StringComparison.Ordinal) &&
            !store.Contains("Retry", StringComparison.Ordinal) &&
            store.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }",
                StringComparison.Ordinal),
            "13C store has no raw SQL, retry, handler or external side effect and propagates cancellation");
        var offline = new ActionExecutionStatePersistenceStore(null!);
        check((await offline.TryReserveInitialAsync(null!, null!, CancellationToken.None)).Status ==
                ActionExecutionStateResultStatus.InvalidRequest &&
            (await offline.TryReserveAsync(null!, CancellationToken.None)).Status ==
                ActionExecutionStateResultStatus.InvalidRequest &&
            (await offline.TryCompleteAsync(null!, CancellationToken.None)).Status ==
                ActionExecutionStateResultStatus.InvalidRequest,
            "13C invalid store requests fail before database access");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var propagated = false;
            try { await offline.TryReserveInitialAsync(null!, null!, cancelled.Token); }
            catch (OperationCanceledException) { propagated = true; }
            check(propagated, "13C cancellation propagates before database access");
        }
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var safety = new RuntimeSafetyOptions();
        check(!startup.ExecutionState && !informational.ExecutionState &&
            !safety.ToolExecutionEnabled && !safety.ProductionAdaptersEnabled && !safety.SafeFileAccessEnabled,
            "13C schema and adapter do not activate production runtime");
    }
}
