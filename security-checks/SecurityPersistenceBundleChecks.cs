using Aura.Api.Security;
using Aura.Application.Auditing;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Verifications;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Data.Migrations;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class SecurityPersistenceBundleChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector()).Options;
        await using var db = new AuraDbContext(options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var audit = model.FindEntityType(typeof(SecurityAuditEventRecord))!;
        var reconciliation = model.FindEntityType(typeof(ExecutionReconciliationAssessmentRecord))!;
        var verification = model.FindEntityType(typeof(ExecutionVerificationAssessmentRecord))!;
        static HashSet<string> Fields(IEntityType entity) =>
            entity.GetProperties().Select(x => x.Name).ToHashSet();
        static Dictionary<string, string> Constraints(IEntityType entity) =>
            entity.GetCheckConstraints().ToDictionary(x => x.Name!, x => x.Sql!);
        check(audit.GetTableName() == "SecurityAuditEvents" &&
            audit.FindPrimaryKey()!.Properties.Single().Name == "Id" &&
            db.SecurityAuditEvents is not null &&
            db.Model.FindEntityType(typeof(AuditLog))!.GetTableName() == "AuditLogs",
            "13E dedicated audit table preserves generic AuditLogs");
        check(Fields(audit).SetEquals(new[] { "Id", "EventType", "Outcome", "OccurredAt",
            "UserId", "ConversationId", "ProjectId", "ActionId", "ToolId", "ApprovalId",
            "ExecutionId", "VerificationId", "ReconciliationId", "PermissionGrantId" }),
            "13E audit carries only exact typed scope and provenance fields");
        foreach (var name in new[] { "Id", "UserId", "ConversationId", "ProjectId" })
            check(audit.FindProperty(name) is { IsNullable: false } &&
                Constraints(audit)["CK_SecurityAuditEvents_Ids"].Contains("\"" + name + "\" <>"),
                "13E audit required nonempty " + name);
        foreach (var name in new[] { "ActionId", "ApprovalId", "ExecutionId", "VerificationId",
            "ReconciliationId", "PermissionGrantId" })
            check(audit.FindProperty(name)!.IsNullable &&
                Constraints(audit)["CK_SecurityAuditEvents_Ids"].Contains("\"" + name + "\" IS NULL"),
                "13E optional typed audit reference " + name);
        check(audit.FindProperty("ToolId")!.GetMaxLength() == 64 &&
            Constraints(audit)["CK_SecurityAuditEvents_ToolId"].Contains("^[a-z][a-z0-9-]{0,63}$") &&
            Constraints(audit)["CK_SecurityAuditEvents_Enums"].Contains("BETWEEN 0 AND 11") &&
            Constraints(audit)["CK_SecurityAuditEvents_Enums"].Contains("BETWEEN 0 AND 3") &&
            !audit.FindProperty("OccurredAt")!.IsNullable,
            "13E audit tool, event, outcome and time are bounded");
        check(Enum.GetValues<AuditEventType>().Select(x => (int)x).SequenceEqual(Enumerable.Range(0, 12)) &&
            Enum.GetValues<AuditEventOutcome>().Select(x => (int)x).SequenceEqual(Enumerable.Range(0, 4)) &&
            Enum.GetValues<ExecutionReconciliationReason>().Select(x => (int)x).SequenceEqual(Enumerable.Range(0, 6)) &&
            (int)ExecutionReconciliationDisposition.NeedsManualReview == 0 &&
            (int)ExecutionReconciliationDisposition.SafeToMarkFailed == 1 &&
            (int)VerificationOutcome.Verified == 0 && (int)VerificationOutcome.Failed == 1 &&
            (int)VerificationOutcome.Inconclusive == 2,
            "13E persisted numeric constraints match current application enum values");
        check(!Fields(audit).Overlaps(["Description", "OldValue", "NewValue", "Metadata", "Json"]) &&
            !audit.GetSeedData().Any(), "13E audit has no generic text/JSON payload or seed");

        check(reconciliation.GetTableName() == "ExecutionReconciliationAssessments" &&
            reconciliation.FindPrimaryKey()!.Properties.Single().Name == "Id" &&
            db.ExecutionReconciliationAssessments is not null,
            "13E dedicated reconciliation history has unique assessment ID");
        check(Fields(reconciliation).SetEquals(new[] { "Id", "ActionId", "UserId", "ConversationId",
            "ProjectId", "ToolId", "ApprovalId", "ExecutionId", "ObservedStatus",
            "ObservedVersion", "Reason", "Disposition", "AssessedAt" }),
            "13E reconciliation assessment carries exact bounded bindings and observation");
        check(reconciliation.FindProperty("ToolId")!.GetMaxLength() == 64 &&
            Constraints(reconciliation)["CK_ExecutionReconciliationAssessments_ToolId"]
                .Contains("^[a-z][a-z0-9-]{0,63}$") &&
            reconciliation.FindProperty("ObservedStatus")!.ClrType == typeof(DurableExecutionStatus) &&
            reconciliation.FindProperty("ObservedVersion")!.ClrType == typeof(long) &&
            Constraints(reconciliation)["CK_ExecutionReconciliationAssessments_State"]
                .Contains("\"ObservedStatus\" = 1") &&
            Constraints(reconciliation)["CK_ExecutionReconciliationAssessments_State"]
                .Contains("\"Disposition\" IN (0, 1)"),
            "13E reconciliation stores only Reserved v1 and bounded reason/disposition");
        foreach (var name in new[] { "Id", "ActionId", "UserId", "ConversationId", "ProjectId",
            "ApprovalId", "ExecutionId" })
            check(reconciliation.FindProperty(name) is { IsNullable: false } &&
                Constraints(reconciliation)["CK_ExecutionReconciliationAssessments_Ids"]
                    .Contains("\"" + name + "\" <>"),
                "13E reconciliation required nonempty binding " + name);
        check(!reconciliation.FindProperty("AssessedAt")!.IsNullable &&
            reconciliation.FindProperty("Reason")!.ClrType == typeof(int) &&
            reconciliation.FindProperty("Disposition")!.ClrType == typeof(int),
            "13E reconciliation assessment time and bounded numeric decisions required");
        check(!reconciliation.GetIndexes().Any(x => x.IsUnique) &&
            !reconciliation.GetSeedData().Any(),
            "13E reconciliation history has no invented per-execution uniqueness or seed");

        check(verification.GetTableName() == "ExecutionVerificationAssessments" &&
            verification.FindPrimaryKey()!.Properties.Single().Name == "Id" &&
            db.ExecutionVerificationAssessments is not null &&
            db.Model.FindEntityType(typeof(Verification))!.GetTableName() == "Verifications",
            "13E dedicated verification table preserves generic Verifications");
        check(Fields(verification).SetEquals(new[] { "Id", "ActionId", "UserId", "ConversationId",
            "ProjectId", "ToolId", "ApprovalId", "ExecutionId", "ExecutionStatus", "Outcome",
            "ProofSource", "ProofVersion", "AssessedAt" }),
            "13E verification carries exact execution, scope, assessment and proof marker fields");
        check(verification.FindProperty("ToolId")!.GetMaxLength() == 64 &&
            Constraints(verification)["CK_ExecutionVerificationAssessments_ToolId"]
                .Contains("^[a-z][a-z0-9-]{0,63}$") &&
            Constraints(verification)["CK_ExecutionVerificationAssessments_State"]
                .Contains("\"ExecutionStatus\" IN (2, 3)") &&
            Constraints(verification)["CK_ExecutionVerificationAssessments_State"]
                .Contains("\"Outcome\" IN (1, 2)") &&
            Constraints(verification)["CK_ExecutionVerificationAssessments_State"]
                .Contains("\"ProofSource\" = 0 AND \"ProofVersion\" = 0") &&
            !Fields(verification).Overlaps(["ActualResult", "Details", "Metadata", "Json"]),
            "13E terminal verification cannot assert Verified without independent proof");
        check(!verification.GetIndexes().Any(x => x.IsUnique) &&
            !verification.GetSeedData().Any(),
            "13E no invented single-verification-per-execution rule or seed");
        foreach (var name in new[] { "Id", "ActionId", "UserId", "ConversationId", "ProjectId",
            "ApprovalId", "ExecutionId" })
            check(verification.FindProperty(name) is { IsNullable: false } &&
                Constraints(verification)["CK_ExecutionVerificationAssessments_Ids"]
                    .Contains("\"" + name + "\" <>"),
                "13E verification required nonempty binding " + name);
        check(!verification.FindProperty("AssessedAt")!.IsNullable &&
            verification.FindProperty("ProofSource")!.ClrType == typeof(VerificationProofSource),
            "13E verification assessment timestamp and proof-source type required");

        foreach (var entity in new[] { audit, reconciliation, verification })
        {
            var expected = entity == audit
                ? new[] { typeof(User), typeof(Conversation), typeof(Project) }
                : new[] { typeof(ActionExecutionStateRecord), typeof(User), typeof(Conversation),
                    typeof(Project), typeof(ActionApproval) };
            check(entity.GetForeignKeys().Count() == expected.Length &&
                expected.All(type => entity.GetForeignKeys().Any(fk =>
                    fk.PrincipalEntityType.ClrType == type &&
                    fk.DeleteBehavior == DeleteBehavior.Restrict &&
                    fk.Properties.Single().IsNullable == false)),
                "13E required provenance FKs all restrict deletes: " + entity.GetTableName());
            check(!entity.GetForeignKeys().Any(fk => fk.DeleteBehavior == DeleteBehavior.SetNull),
                "13E required bindings cannot be nulled by delete: " + entity.GetTableName());
        }
        var migration = new AddSecurityPersistenceBundle();
        check(migration.UpOperations.Count(x => x is CreateTableOperation) == 3 &&
            migration.UpOperations.All(x => x is CreateTableOperation or CreateIndexOperation) &&
            migration.DownOperations.OfType<DropTableOperation>().Select(x => x.Name).ToHashSet()
                .SetEquals(["SecurityAuditEvents", "ExecutionReconciliationAssessments",
                    "ExecutionVerificationAssessments"]) && migration.DownOperations.Count == 3,
            "13E migration creates and rolls back only three new tables and indexes");
        foreach (var (type, table) in new[] {
            (typeof(SecurityAuditEventRecord), "SecurityAuditEvents"),
            (typeof(ExecutionReconciliationAssessmentRecord), "ExecutionReconciliationAssessments"),
            (typeof(ExecutionVerificationAssessmentRecord), "ExecutionVerificationAssessments") })
        {
            var operation = migration.UpOperations.OfType<CreateTableOperation>().Single(x => x.Name == table);
            check(operation.Columns.Select(x => x.Name).ToHashSet().SetEquals(
                    Fields(model.FindEntityType(type)!)) &&
                operation.CheckConstraints.Count == 3 &&
                operation.ForeignKeys.All(x => x.OnDelete ==
                    Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict),
                "13E compiled migration matches exact table fields and restrictions: " + table);
        }
        var root = Directory.GetCurrentDirectory();
        var program = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var migrationText = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Data",
            "Migrations", "20261014000002_AddSecurityPersistenceBundle.cs"));
        var reconciliationSource = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure",
            "Services", "ExecutionReconciliationPersistenceSource.cs"));
        var auditSink = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure",
            "Services", "SecurityAuditPersistenceSink.cs"));
        check(!program.Contains("SecurityAuditPersistenceSink", StringComparison.Ordinal) &&
            !program.Contains("ExecutionReconciliationPersistenceSource", StringComparison.Ordinal) &&
            !program.Contains("ExecutionVerificationAssessmentRecord", StringComparison.Ordinal) &&
            !program.Contains("Migrate()", StringComparison.Ordinal) &&
            !program.Contains("MigrateAsync(", StringComparison.Ordinal),
            "13E no production registration or migration auto-apply");
        check(!migrationText.Contains("AlterTable(", StringComparison.Ordinal) &&
            !migrationText.Contains("InsertData(", StringComparison.Ordinal) &&
            !migrationText.Contains("Sql(", StringComparison.Ordinal) &&
            !migrationText.Contains("ToolExecutions", StringComparison.Ordinal) &&
            !migrationText.Contains("AuditLogs", StringComparison.Ordinal) &&
            !migrationText.Contains("name: \"Verifications\"", StringComparison.Ordinal),
            "13E no legacy table reinterpretation, data copy or seed");
        check(reconciliationSource.Contains("db.ActionExecutionStates.AsNoTracking()", StringComparison.Ordinal) &&
            reconciliationSource.Contains("SingleOrDefaultAsync", StringComparison.Ordinal) &&
            reconciliationSource.Contains("IsolationLevel.RepeatableRead", StringComparison.Ordinal) &&
            !reconciliationSource.Contains("ToolExecutions", StringComparison.Ordinal) &&
            !reconciliationSource.Contains("ExecuteUpdateAsync", StringComparison.Ordinal) &&
            !reconciliationSource.Contains("SaveChangesAsync", StringComparison.Ordinal) &&
            !reconciliationSource.Contains("handler.", StringComparison.Ordinal),
            "13E reconciliation source uses exact read-only durable state without handler retry");
        check(auditSink.Contains("db.SecurityAuditEvents.Add", StringComparison.Ordinal) &&
            auditSink.Contains("SaveChangesAsync(cancellationToken)", StringComparison.Ordinal) &&
            !auditSink.Contains("AuditLogs", StringComparison.Ordinal) &&
            !auditSink.Contains("Retry", StringComparison.Ordinal),
            "13E direct audit sink inserts bounded event once without legacy fallback or retry");
        var sink = new SecurityAuditPersistenceSink(null!);
        var source = new ExecutionReconciliationPersistenceSource(null!);
        check(await sink.WriteAsync(null!, default) == AuditSinkStatus.Failed &&
            await source.GetAsync(null!, null!, default) is null,
            "13E invalid adapter requests fail before database access");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var sinkCancelled = false;
            var sourceCancelled = false;
            try { await sink.WriteAsync(null!, cancelled.Token); }
            catch (OperationCanceledException) { sinkCancelled = true; }
            try { await source.GetAsync(null!, null!, cancelled.Token); }
            catch (OperationCanceledException) { sourceCancelled = true; }
            check(sinkCancelled && sourceCancelled, "13E adapters propagate pre-cancelled tokens");
        }
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var safety = new RuntimeSafetyOptions();
        check(!startup.AuditSink && !startup.Reconciliation && !startup.Verification &&
            !startup.ExecutionState && !informational.AuditSink && !informational.Reconciliation &&
            !informational.Verification && !informational.ExecutionState &&
            !safety.ToolExecutionEnabled && !safety.ProductionAdaptersEnabled &&
            !safety.SafeFileAccessEnabled,
            "13E typed foundations do not inflate readiness or enable production execution");
    }
}
