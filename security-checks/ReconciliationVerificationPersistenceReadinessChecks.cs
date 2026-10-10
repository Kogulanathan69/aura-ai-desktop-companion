using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

internal static class ReconciliationVerificationPersistenceReadinessChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector())
            .Options;
        await using var db = new AuraDbContext(options);
        var tables = db.Model.GetEntityTypes().Select(entity => entity.GetTableName()).ToHashSet();
        check(!tables.Any(table => table?.Contains("Reconciliation", StringComparison.OrdinalIgnoreCase) == true),
            "12J no durable reconciliation assessment table exists");

        var verification = db.Model.FindEntityType(typeof(Verification))!;
        var fields = verification.GetProperties().Select(property => property.Name).ToHashSet();
        check(verification.GetTableName() == "Verifications" &&
            verification.FindPrimaryKey()!.Properties.Single().Name == nameof(Verification.Id) &&
            fields.Contains(nameof(Verification.AIActionId)) &&
            verification.FindProperty(nameof(Verification.ToolExecutionId))!.IsNullable,
            "12J generic verification row has action link and optional tool execution link");
        foreach (var missing in new[] { "UserId", "ConversationId", "ProjectId", "ToolId", "ApprovalId",
            "ExecutionId", "Outcome", "IssuedAt", "ProofSource", "ProofVersion" })
            check(!fields.Contains(missing), "12J verification evidence field absent: " + missing);
        check(verification.FindProperty(nameof(Verification.VerificationType))!.GetMaxLength() == 50 &&
            verification.FindProperty(nameof(Verification.Status))!.GetMaxLength() == 30 &&
            verification.FindProperty(nameof(Verification.ExpectedResult))!.GetColumnType() == "text" &&
            verification.FindProperty(nameof(Verification.ActualResult))!.GetColumnType() == "text" &&
            verification.FindProperty(nameof(Verification.Details))!.GetColumnType() == "text",
            "12J generic status and free text cannot attest bounded independent proof");
        check(!verification.GetProperties().Any(property => property.IsConcurrencyToken) &&
            !verification.GetIndexes().Any(index => index.IsUnique &&
                index.Properties.Any(property => property.Name == nameof(Verification.ToolExecutionId))),
            "12J no versioned or unique execution assessment constraint exists");

        var implementations = typeof(AuraDbContext).Assembly.GetTypes()
            .Concat(typeof(IExecutionReconciliationSource).Assembly.GetTypes())
            .Concat(typeof(RuntimeSafetyOptions).Assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract).ToArray();
        check(implementations.Where(type => typeof(IExecutionReconciliationSource).IsAssignableFrom(type))
                .SequenceEqual([typeof(UnavailableExecutionReconciliationSource)]) &&
            implementations.Where(type => typeof(IExecutionEvidenceSource).IsAssignableFrom(type))
                .SequenceEqual([typeof(UnavailableExecutionEvidenceSource)]) &&
            implementations.Where(type => typeof(IIndependentVerificationPolicy).IsAssignableFrom(type))
                .SequenceEqual([typeof(ConservativeVerificationPolicy)]),
            "12J no production reconciliation, execution evidence, or independent proof adapter");
        check(await new UnavailableExecutionReconciliationSource().GetAsync(null!, null!, CancellationToken.None) is null &&
            await new UnavailableExecutionEvidenceSource().GetAsync(null!, Guid.Empty, CancellationToken.None) is null,
            "12J default sources provide no evidence");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var reconciliationCancelled = false;
            var verificationCancelled = false;
            try { await new UnavailableExecutionReconciliationSource().GetAsync(null!, null!, cancelled.Token); }
            catch (OperationCanceledException) { reconciliationCancelled = true; }
            try { await new UnavailableExecutionEvidenceSource().GetAsync(null!, Guid.Empty, cancelled.Token); }
            catch (OperationCanceledException) { verificationCancelled = true; }
            check(reconciliationCancelled && verificationCancelled, "12J unavailable sources propagate cancellation");
        }
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var defaults = new RuntimeSafetyOptions();
        check(!startup.Reconciliation && !startup.Verification &&
            !informational.Reconciliation && !informational.Verification &&
            !defaults.ProductionAdaptersEnabled && !defaults.ToolExecutionEnabled &&
            !defaults.SafeFileAccessEnabled,
            "12J reconciliation and verification readiness remain disabled");
    }
}
