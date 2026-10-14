using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

internal static class CompositeBindingIntegrityChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=metadata_only",
                npgsql => npgsql.UseVector())
            .Options;

        await using var db = new AuraDbContext(options);

        var model = db.GetService<IDesignTimeModel>().Model;

        var execution =
            model.FindEntityType(typeof(ActionExecutionStateRecord))!;

        var reconciliation =
            model.FindEntityType(
                typeof(ExecutionReconciliationAssessmentRecord))!;

        var verification =
            model.FindEntityType(
                typeof(ExecutionVerificationAssessmentRecord))!;

        check(
            execution.FindIndex(
                execution.FindProperty("ApprovalId")!)?.IsUnique == true,
            "13F execution state keeps one durable state per approval");

        check(
            execution.GetIndexes().Any(index =>
                index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name == "ExecutionId"),
            "13F execution ID remains uniquely bound when present");

        var requiredBindingNames = new[]
        {
            "ActionId",
            "UserId",
            "ConversationId",
            "ProjectId",
            "ApprovalId",
            "ExecutionId"
        };

        foreach (var entity in new[]
                 {
                     reconciliation,
                     verification
                 })
        {
            check(
                requiredBindingNames.All(name =>
                    entity.FindProperty(name) is
                    {
                        IsNullable: false
                    }),
                $"13F required exact bindings remain non-null: {entity.GetTableName()}");

            check(
                entity.GetForeignKeys().All(fk =>
                    fk.Properties.Count == 1),
                $"13F current database FKs remain single-column provenance references: {entity.GetTableName()}");

            check(
                !entity.GetIndexes().Any(index =>
                    index.Properties
                        .Select(property => property.Name)
                        .ToHashSet(StringComparer.Ordinal)
                        .IsSupersetOf(requiredBindingNames)),
                $"13F no false composite-chain enforcement claim exists: {entity.GetTableName()}");
        }

        var root = Directory.GetCurrentDirectory();

        var source = File.ReadAllText(Path.Combine(
            root,
            "Aura.Infrastructure",
            "Services",
            "ExecutionReconciliationPersistenceSource.cs"));

        check(
            source.Contains(
                "x.ActionId == actionId.Value",
                StringComparison.Ordinal) &&
            source.Contains(
                "x.UserId == scope.UserId",
                StringComparison.Ordinal) &&
            source.Contains(
                "x.ConversationId == scope.ConversationId",
                StringComparison.Ordinal) &&
            source.Contains(
                "x.ProjectId == scope.ProjectId",
                StringComparison.Ordinal),
            "13F reconciliation source reads exact durable action and ownership scope");

        check(
            source.Contains(
                "x.AIActionId == actionId.Value",
                StringComparison.Ordinal) &&
            source.Contains(
                "x.Id == row.ApprovalId",
                StringComparison.Ordinal),
            "13F reconciliation source independently rechecks action and approval ownership chain");

        check(
            !source.Contains(
                "SaveChangesAsync",
                StringComparison.Ordinal) &&
            !source.Contains(
                "ExecuteUpdateAsync",
                StringComparison.Ordinal),
            "13F composite-chain validation path remains read-only");

        check(
            source.Contains(
                "IsolationLevel.RepeatableRead",
                StringComparison.Ordinal),
            "13F reconciliation chain reads remain bounded by repeatable-read transaction");
    }
}