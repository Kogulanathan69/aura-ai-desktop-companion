using Npgsql;

// Explicit, read-only catalog inspection. The older Step 13K harness can apply a
// migration and write fixtures, so its execution path is deliberately not reused.
internal static class ProductionMigrationBaselineEvidence
{
    internal const string Command = "--live-production-migration-baseline-evidence";
    internal const string EnableVariable = "AURA_LIVE_PRODUCTION_MIGRATION_BASELINE_EVIDENCE";
    internal const string DisposableVariable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";
    internal const string ConnectionVariable = "ConnectionStrings__DefaultConnection";
    private const string DisposablePrefix = "aura_disposable_";

    internal static readonly string[] ExpectedMigrations =
    [
        "20261014000000_AddToolPermissionGrants",
        "20261014000001_AddActionExecutionStates",
        "20261014000002_AddSecurityPersistenceBundle",
        "20261014000003_AddSecurityAuditOutbox",
        "20261014000004_HardenSecurityAuditOutboxDelivery",
        "20261018000000_AddTrustedVerificationFoundation",
        "20261019000000_AddIndependentVerificationOutcome",
        "20261019000001_AddProjectVersion"
    ];
    internal const string HardenedDeliveryExpression =
        "(\"DeliveryState\" = 0 AND \"AttemptCount\" = 0 AND \"DeliveredAt\" IS NULL) OR " +
        "(\"DeliveryState\" = 1 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NOT NULL) OR " +
        "(\"DeliveryState\" = 2 AND \"AttemptCount\" > 0 AND \"DeliveredAt\" IS NULL)";
    internal const string VerificationFoundationExpression =
        "\"ExecutionStatus\" IN (2, 3) AND \"Outcome\" IN (1, 2) AND " +
        "((\"ExecutionVersion\" IS NULL AND \"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
        "(\"ExecutionVersion\" = 2 AND ((\"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
        "(\"ProofSource\" = 1 AND \"ProofVersion\" = 1))))";
    internal const string IndependentVerificationOutcomeExpression =
        "(\"ExecutionStatus\" IN (2, 3) AND \"Outcome\" IN (1, 2) AND " +
        "((\"ExecutionVersion\" IS NULL AND \"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
        "(\"ExecutionVersion\" = 2 AND ((\"ProofSource\" = 0 AND \"ProofVersion\" = 0) OR " +
        "(\"ProofSource\" = 1 AND \"ProofVersion\" = 1))))) OR " +
        "(\"ExecutionStatus\" = 2 AND \"Outcome\" = 0 AND \"ExecutionVersion\" = 2 AND " +
        "\"ProofSource\" = 1 AND \"ProofVersion\" = 1)";

    internal enum Stage
    {
        Passed, Connectivity, MissingHistory, DuplicateMigration, MissingMigration,
        UnexpectedMigration, MigrationOrder, MissingTable, MissingColumn, MissingTrigger,
        IncompatibleColumn, MissingPrimaryKey, MissingForeignKey,
        MissingUniqueIndex, MissingCheckConstraint, IncompatibleConstraint,
        Unexpected
    }

    internal sealed record Column(string Table, string Name, string Type, bool Required,
        int? MaxLength = null, string? DefaultValue = null);
    internal sealed record Trigger(string Name, string Definition, string FunctionDefinition);
    internal sealed record Constraint(string Table, string Name, char Kind,
        string? ReferencedTable, char DeleteAction, bool Validated, string Definition);
    internal sealed record Index(string Table, string Name, bool Unique, string Definition);
    internal sealed record Snapshot(bool HistoryExists, IReadOnlyList<string> History,
        IReadOnlySet<string> Tables, IReadOnlyList<Column> Columns,
        IReadOnlyList<Constraint> Constraints, IReadOnlyList<Index> Indexes,
        IReadOnlyList<Trigger>? Triggers = null);

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        Func<string, CancellationToken, Task<Stage>>? execute = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (environment(EnableVariable) != "true" ||
                environment(DisposableVariable) != "true")
            {
                write("PRODUCTION MIGRATION BASELINE: SKIPPED");
                return 0;
            }
            var connection = environment(ConnectionVariable);
            if (!ValidConnection(connection))
            {
                write("INPUT: FAIL");
                write("RESULT: FAIL");
                return 1;
            }
            var stage = await (execute ?? ExecuteLiveAsync)(connection!, cancellationToken);
            if (stage == Stage.Passed)
            {
                foreach (var category in new[] { "DB CONNECTIVITY", "MIGRATION HISTORY",
                    "MIGRATION BASELINE", "CORE TABLES", "KEY CONSTRAINTS", "SECURITY SCHEMA" })
                    write(category + ": PASS");
                write("RESULT: PASS");
                return 0;
            }
            write(stage switch
            {
                Stage.Connectivity => "DB CONNECTIVITY: FAIL",
                Stage.MissingHistory => "MIGRATION HISTORY: FAIL",
                Stage.DuplicateMigration or Stage.MissingMigration or
                    Stage.UnexpectedMigration or Stage.MigrationOrder => "MIGRATION BASELINE: FAIL",
                Stage.MissingTable => "CORE TABLES: FAIL",
                Stage.MissingPrimaryKey or Stage.MissingForeignKey or
                    Stage.MissingUniqueIndex or Stage.MissingCheckConstraint or
                    Stage.IncompatibleConstraint => "KEY CONSTRAINTS: FAIL",
                Stage.MissingColumn or Stage.IncompatibleColumn or Stage.MissingTrigger => "SECURITY SCHEMA: FAIL",
                _ => "RESULT: FAIL"
            });
            write("RESULT: FAIL");
            return 1;
        }
        catch (Exception)
        {
            write("RESULT: FAIL");
            return 1;
        }
    }

    private static bool ValidConnection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(value);
            return !string.IsNullOrWhiteSpace(parsed.Host) &&
                !string.IsNullOrWhiteSpace(parsed.Username) &&
                parsed.Database?.StartsWith(DisposablePrefix, StringComparison.Ordinal) == true &&
                parsed.Database.Length > DisposablePrefix.Length;
        }
        catch (Exception) { return false; }
    }

    private static async Task<Stage> ExecuteLiveAsync(string connection,
        CancellationToken cancellationToken)
    {
        await using var db = new NpgsqlConnection(connection);
        try { await db.OpenAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Stage.Connectivity; }
        var snapshot = await ReadSnapshotAsync(db, cancellationToken);
        return Evaluate(snapshot);
    }

    private static async Task<Snapshot> ReadSnapshotAsync(NpgsqlConnection db,
        CancellationToken token)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await ReadAsync(db, "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'",
            row => tables.Add(row.GetString(0)), token);
        var historyExists = tables.Contains("__EFMigrationsHistory");
        var history = new List<string>();
        if (historyExists)
            await ReadAsync(db, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" " +
                "ORDER BY \"MigrationId\"", row => history.Add(row.GetString(0)), token);

        var columns = new List<Column>();
        await ReadAsync(db, "SELECT table_name, column_name, udt_name, is_nullable, " +
            "character_maximum_length, column_default FROM information_schema.columns " +
            "WHERE table_schema = current_schema()",
            row => columns.Add(new(row.GetString(0), row.GetString(1), row.GetString(2),
                row.GetString(3) == "NO", row.IsDBNull(4) ? null : row.GetInt32(4),
                row.IsDBNull(5) ? null : row.GetString(5))), token);

        var triggers = new List<Trigger>();
        await ReadAsync(db, "SELECT trigger_row.tgname, pg_get_triggerdef(trigger_row.oid), " +
            "pg_get_functiondef(trigger_row.tgfoid) FROM pg_catalog.pg_trigger trigger_row " +
            "JOIN pg_catalog.pg_class relation ON relation.oid = trigger_row.tgrelid " +
            "JOIN pg_catalog.pg_namespace namespace_row ON namespace_row.oid = relation.relnamespace " +
            "WHERE namespace_row.nspname = current_schema() AND relation.relname = 'Projects' " +
            "AND NOT trigger_row.tgisinternal AND trigger_row.tgenabled = 'O'",
            row => triggers.Add(new(row.GetString(0), row.GetString(1), row.GetString(2))), token);

        var constraints = new List<Constraint>();
        await ReadAsync(db, "SELECT relation.relname, constraint_row.conname, " +
            "constraint_row.contype::text, referenced.relname, " +
            "constraint_row.confdeltype::text, constraint_row.convalidated, " +
            "pg_get_constraintdef(constraint_row.oid) " +
            "FROM pg_catalog.pg_constraint constraint_row " +
            "JOIN pg_catalog.pg_class relation ON relation.oid = constraint_row.conrelid " +
            "JOIN pg_catalog.pg_namespace namespace_row ON namespace_row.oid = relation.relnamespace " +
            "LEFT JOIN pg_catalog.pg_class referenced ON referenced.oid = constraint_row.confrelid " +
            "WHERE namespace_row.nspname = current_schema()",
            row => constraints.Add(new(row.GetString(0), row.GetString(1),
                row.GetString(2)[0], row.IsDBNull(3) ? null : row.GetString(3),
                row.GetString(4)[0], row.GetBoolean(5), row.GetString(6))), token);

        var indexes = new List<Index>();
        await ReadAsync(db, "SELECT relation.relname, index_row.relname, " +
            "index_data.indisunique, pg_get_indexdef(index_data.indexrelid) " +
            "FROM pg_catalog.pg_index index_data " +
            "JOIN pg_catalog.pg_class relation ON relation.oid = index_data.indrelid " +
            "JOIN pg_catalog.pg_class index_row ON index_row.oid = index_data.indexrelid " +
            "JOIN pg_catalog.pg_namespace namespace_row ON namespace_row.oid = relation.relnamespace " +
            "WHERE namespace_row.nspname = current_schema()",
            row => indexes.Add(new(row.GetString(0), row.GetString(1),
                row.GetBoolean(2), row.GetString(3))), token);
        return new(historyExists, history, tables, columns, constraints, indexes, triggers);
    }

    private static async Task ReadAsync(NpgsqlConnection db, string sql,
        Action<NpgsqlDataReader> consume, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, db);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) consume(reader);
    }

    internal static Stage Evaluate(Snapshot snapshot)
    {
        if (!snapshot.HistoryExists) return Stage.MissingHistory;
        if (snapshot.History.Count != snapshot.History.Distinct(StringComparer.Ordinal).Count())
            return Stage.DuplicateMigration;
        if (ExpectedMigrations.Any(id => !snapshot.History.Contains(id, StringComparer.Ordinal)))
            return Stage.MissingMigration;
        if (snapshot.History.Any(id => !ExpectedMigrations.Contains(id, StringComparer.Ordinal)))
            return Stage.UnexpectedMigration;
        if (!snapshot.History.SequenceEqual(ExpectedMigrations))
            return Stage.MigrationOrder;

        foreach (var table in RequiredTables)
            if (!snapshot.Tables.Contains(table)) return Stage.MissingTable;
        foreach (var expected in RequiredColumns)
        {
            var actual = snapshot.Columns.FirstOrDefault(x => x.Table == expected.Table &&
                x.Name == expected.Name);
            if (actual is null) return Stage.MissingColumn;
            if (actual.Type != expected.Type || actual.Required != expected.Required ||
                (expected.MaxLength is not null && actual.MaxLength != expected.MaxLength))
                return Stage.IncompatibleColumn;
        }
        var projectVersion = snapshot.Columns.SingleOrDefault(x => x.Table == "Projects" &&
            x.Name == "Version");
        if (projectVersion is null || Compact(projectVersion.DefaultValue ?? "") != "1")
            return Stage.IncompatibleColumn;
        var versionTrigger = snapshot.Triggers?.SingleOrDefault(x =>
            x.Name == "TR_Projects_IncrementVersion");
        if (versionTrigger is null ||
            !Compact(versionTrigger.Definition).Contains("beforeinsertorupdateon", StringComparison.Ordinal) ||
            !Compact(versionTrigger.Definition).Contains("aura_increment_project_version", StringComparison.Ordinal) ||
            !Compact(versionTrigger.FunctionDefinition).Contains("new.versionisdistinctfrom1", StringComparison.Ordinal) ||
            !Compact(versionTrigger.FunctionDefinition).Contains("new.version:=old.version+1", StringComparison.Ordinal) ||
            !Compact(versionTrigger.FunctionDefinition).Contains("new.versionisdistinctfromold.version", StringComparison.Ordinal) ||
            !Compact(versionTrigger.FunctionDefinition).Contains("old.version=9223372036854775807", StringComparison.Ordinal))
            return Stage.MissingTrigger;
        foreach (var (table, key) in PrimaryKeys)
        {
            var actual = snapshot.Constraints.FirstOrDefault(x => x.Table == table &&
                x.Name == "PK_" + table && x.Kind == 'p' && x.Validated);
            if (actual is null || !Compact(actual.Definition).Contains("primarykey" + key,
                    StringComparison.Ordinal)) return Stage.MissingPrimaryKey;
        }
        foreach (var (table, column, target, action) in ForeignKeys)
        {
            var name = PostgreSqlIdentifier("FK_" + table + "_" + target + "_" + column);
            var actual = snapshot.Constraints.FirstOrDefault(x => x.Table == table &&
                x.Name == name && x.Kind == 'f' && x.Validated);
            if (actual is null || actual.ReferencedTable != target ||
                actual.DeleteAction != action ||
                !Compact(actual.Definition).Contains("foreignkey" + column.ToLowerInvariant(),
                    StringComparison.Ordinal)) return Stage.MissingForeignKey;
        }
        foreach (var (table, name, columns, predicate) in UniqueIndexes)
        {
            var actual = snapshot.Indexes.FirstOrDefault(x => x.Table == table &&
                x.Name == name && x.Unique);
            if (actual is null) return Stage.MissingUniqueIndex;
            var definition = Compact(actual.Definition);
            if (!definition.Contains(columns, StringComparison.Ordinal) ||
                (predicate is not null && !definition.Contains(predicate, StringComparison.Ordinal)))
                return Stage.MissingUniqueIndex;
        }
        foreach (var (table, name, markers) in CheckConstraints)
        {
            var actual = snapshot.Constraints.FirstOrDefault(x => x.Table == table &&
                x.Name == name && x.Kind == 'c' && x.Validated);
            if (actual is null) return Stage.MissingCheckConstraint;
            var definition = Compact(actual.Definition);
            if (!definition.StartsWith("check", StringComparison.Ordinal) ||
                definition.Contains("checktrue", StringComparison.Ordinal) ||
                markers.Any(marker => !definition.Contains(marker, StringComparison.Ordinal)))
                return Stage.IncompatibleConstraint;
            if (name == "CK_SecurityAuditOutbox_Delivery" &&
                CanonicalExpression(actual.Definition) !=
                CanonicalExpression("CHECK " + HardenedDeliveryExpression))
                return Stage.IncompatibleConstraint;
            if (name == "CK_ExecutionVerificationAssessments_State" &&
                (!definition.Contains("executionversionisnullandproofsource=0andproofversion=0",
                    StringComparison.Ordinal) ||
                 !definition.Contains("executionversion=2andproofsource=0andproofversion=0",
                    StringComparison.Ordinal) ||
                 !definition.Contains("proofsource=1andproofversion=1",
                    StringComparison.Ordinal) ||
                 !definition.Contains("executionstatus=2andoutcome=0andexecutionversion=2andproofsource=1andproofversion=1",
                    StringComparison.Ordinal) ||
                 definition.Split("outcome=0", StringSplitOptions.None).Length != 2))
                return Stage.IncompatibleConstraint;
        }
        return Stage.Passed;
    }

    // PostgreSQL stores identifiers in at most 63 bytes. All generated names
    // validated by this verifier are ASCII, so character truncation is equivalent.
    private static string PostgreSqlIdentifier(string value) =>
        value.Length <= 63 ? value : value[..63];

    // Presentation normalization only. Constraint marker checks are shape evidence,
    // not a proof of SQL logical equivalence or historical migration chronology.
    private static string Compact(string value) => new(value.Where(c =>
        !char.IsWhiteSpace(c) && c is not '"' and not '(' and not ')').Select(char.ToLowerInvariant)
        .ToArray());

    private static string CanonicalExpression(string value) => new(value.Where(c =>
        !char.IsWhiteSpace(c) && c is not '(' and not ')').ToArray());

    internal static readonly string[] RequiredTables =
    [
        "Users", "Projects", "Conversations", "ToolPermissionGrants", "AIActions",
        "ActionApprovals", "ActionExecutionStates", "SecurityAuditEvents",
        "SecurityAuditOutbox", "ExecutionReconciliationAssessments",
        "ExecutionVerificationAssessments"
    ];

    internal static readonly Column[] RequiredColumns =
    [
        new("Users", "Id", "uuid", true), new("Users", "AuthUserId", "uuid", true),
        new("Users", "IsActive", "bool", true),
        new("Projects", "Id", "uuid", true), new("Projects", "UserId", "uuid", true),
        new("Projects", "Version", "int8", true, DefaultValue: "1"),
        new("Conversations", "Id", "uuid", true), new("Conversations", "UserId", "uuid", true),
        new("Conversations", "ProjectId", "uuid", false),
        new("Conversations", "Type", "varchar", true, 30),
        new("AIActions", "Id", "uuid", true), new("AIActions", "UserId", "uuid", true),
        new("AIActions", "ProjectId", "uuid", false),
        new("AIActions", "ConversationId", "uuid", false),
        new("ActionApprovals", "Id", "uuid", true),
        new("ActionApprovals", "AIActionId", "uuid", true),
        new("ActionApprovals", "UserId", "uuid", true),
        new("ToolPermissionGrants", "Id", "uuid", true),
        new("ToolPermissionGrants", "UserId", "uuid", true),
        new("ToolPermissionGrants", "ConversationId", "uuid", true),
        new("ToolPermissionGrants", "ProjectId", "uuid", true),
        new("ToolPermissionGrants", "ToolId", "varchar", true, 64),
        new("ToolPermissionGrants", "RequiredPermission", "int4", true),
        new("ToolPermissionGrants", "Status", "int4", true),
        new("ToolPermissionGrants", "RevokedAt", "timestamptz", false),
        new("ToolPermissionGrants", "CreatedAt", "timestamptz", true),
        new("ToolPermissionGrants", "GrantedAt", "timestamptz", true),
        new("ActionExecutionStates", "ActionId", "uuid", true),
        new("ActionExecutionStates", "UserId", "uuid", true),
        new("ActionExecutionStates", "ConversationId", "uuid", true),
        new("ActionExecutionStates", "ProjectId", "uuid", true),
        new("ActionExecutionStates", "ApprovalId", "uuid", true),
        new("ActionExecutionStates", "ExecutionId", "uuid", false),
        new("ActionExecutionStates", "ToolId", "varchar", true, 64),
        new("ActionExecutionStates", "Status", "int4", true),
        new("ActionExecutionStates", "Version", "int8", true),
        new("ActionExecutionStates", "ApprovalConsumed", "bool", true),
        new("ActionExecutionStates", "CreatedAt", "timestamptz", true),
        new("ActionExecutionStates", "ReservedAt", "timestamptz", false),
        new("ActionExecutionStates", "CompletedAt", "timestamptz", false),
        new("SecurityAuditEvents", "Id", "uuid", true),
        new("SecurityAuditEvents", "EventType", "int4", true),
        new("SecurityAuditEvents", "Outcome", "int4", true),
        new("SecurityAuditEvents", "OccurredAt", "timestamptz", true),
        new("SecurityAuditEvents", "UserId", "uuid", true),
        new("SecurityAuditEvents", "ConversationId", "uuid", true),
        new("SecurityAuditEvents", "ProjectId", "uuid", true),
        new("SecurityAuditEvents", "ActionId", "uuid", false),
        new("SecurityAuditEvents", "ToolId", "varchar", false, 64),
        new("SecurityAuditEvents", "ApprovalId", "uuid", false),
        new("SecurityAuditEvents", "ExecutionId", "uuid", false),
        new("SecurityAuditEvents", "VerificationId", "uuid", false),
        new("SecurityAuditEvents", "ReconciliationId", "uuid", false),
        new("SecurityAuditEvents", "PermissionGrantId", "uuid", false),
        new("SecurityAuditOutbox", "Id", "uuid", true),
        new("SecurityAuditOutbox", "AuditEventId", "uuid", true),
        new("SecurityAuditOutbox", "EventType", "int4", true),
        new("SecurityAuditOutbox", "Outcome", "int4", true),
        new("SecurityAuditOutbox", "OccurredAt", "timestamptz", true),
        new("SecurityAuditOutbox", "UserId", "uuid", true),
        new("SecurityAuditOutbox", "ConversationId", "uuid", true),
        new("SecurityAuditOutbox", "ProjectId", "uuid", true),
        new("SecurityAuditOutbox", "ActionId", "uuid", true),
        new("SecurityAuditOutbox", "ApprovalId", "uuid", true),
        new("SecurityAuditOutbox", "ExecutionId", "uuid", true),
        new("SecurityAuditOutbox", "ToolId", "varchar", true, 64),
        new("SecurityAuditOutbox", "DeliveryState", "int4", true),
        new("SecurityAuditOutbox", "AttemptCount", "int4", true),
        new("SecurityAuditOutbox", "DeliveredAt", "timestamptz", false),
        new("ExecutionReconciliationAssessments", "Id", "uuid", true),
        new("ExecutionReconciliationAssessments", "ActionId", "uuid", true),
        new("ExecutionReconciliationAssessments", "UserId", "uuid", true),
        new("ExecutionReconciliationAssessments", "ConversationId", "uuid", true),
        new("ExecutionReconciliationAssessments", "ProjectId", "uuid", true),
        new("ExecutionReconciliationAssessments", "ToolId", "varchar", true, 64),
        new("ExecutionReconciliationAssessments", "ApprovalId", "uuid", true),
        new("ExecutionReconciliationAssessments", "ExecutionId", "uuid", true),
        new("ExecutionReconciliationAssessments", "ObservedStatus", "int4", true),
        new("ExecutionReconciliationAssessments", "ObservedVersion", "int8", true),
        new("ExecutionReconciliationAssessments", "Reason", "int4", true),
        new("ExecutionReconciliationAssessments", "Disposition", "int4", true),
        new("ExecutionReconciliationAssessments", "AssessedAt", "timestamptz", true),
        new("ExecutionVerificationAssessments", "Id", "uuid", true),
        new("ExecutionVerificationAssessments", "ActionId", "uuid", true),
        new("ExecutionVerificationAssessments", "UserId", "uuid", true),
        new("ExecutionVerificationAssessments", "ConversationId", "uuid", true),
        new("ExecutionVerificationAssessments", "ProjectId", "uuid", true),
        new("ExecutionVerificationAssessments", "ToolId", "varchar", true, 64),
        new("ExecutionVerificationAssessments", "ApprovalId", "uuid", true),
        new("ExecutionVerificationAssessments", "ExecutionId", "uuid", true),
        new("ExecutionVerificationAssessments", "ExecutionVersion", "int8", false),
        new("ExecutionVerificationAssessments", "ExecutionStatus", "int4", true),
        new("ExecutionVerificationAssessments", "Outcome", "int4", true),
        new("ExecutionVerificationAssessments", "ProofSource", "int4", true),
        new("ExecutionVerificationAssessments", "ProofVersion", "int4", true),
        new("ExecutionVerificationAssessments", "AssessedAt", "timestamptz", true)
    ];

    internal static readonly (string Table, string Key)[] PrimaryKeys =
        RequiredTables.Select(table => (table, table == "ActionExecutionStates" ? "actionid" : "id"))
            .ToArray();

    internal static readonly (string Table, string Column, string Target, char Action)[] ForeignKeys =
    [
        ("Projects", "UserId", "Users", 'c'),
        ("Conversations", "UserId", "Users", 'c'),
        ("Conversations", "ProjectId", "Projects", 'n'),
        ("AIActions", "UserId", "Users", 'c'),
        ("AIActions", "ProjectId", "Projects", 'n'),
        ("AIActions", "ConversationId", "Conversations", 'n'),
        ("ActionApprovals", "AIActionId", "AIActions", 'c'),
        ("ActionApprovals", "UserId", "Users", 'c'),
        ("ToolPermissionGrants", "UserId", "Users", 'r'),
        ("ToolPermissionGrants", "ConversationId", "Conversations", 'r'),
        ("ToolPermissionGrants", "ProjectId", "Projects", 'r'),
        ("ActionExecutionStates", "ActionId", "AIActions", 'r'),
        ("ActionExecutionStates", "UserId", "Users", 'r'),
        ("ActionExecutionStates", "ConversationId", "Conversations", 'r'),
        ("ActionExecutionStates", "ProjectId", "Projects", 'r'),
        ("ActionExecutionStates", "ApprovalId", "ActionApprovals", 'r'),
        ("SecurityAuditEvents", "UserId", "Users", 'r'),
        ("SecurityAuditEvents", "ConversationId", "Conversations", 'r'),
        ("SecurityAuditEvents", "ProjectId", "Projects", 'r'),
        ("SecurityAuditOutbox", "UserId", "Users", 'r'),
        ("SecurityAuditOutbox", "ConversationId", "Conversations", 'r'),
        ("SecurityAuditOutbox", "ProjectId", "Projects", 'r'),
        ("SecurityAuditOutbox", "ActionId", "AIActions", 'r'),
        ("SecurityAuditOutbox", "ApprovalId", "ActionApprovals", 'r'),
        ("ExecutionReconciliationAssessments", "ActionId", "ActionExecutionStates", 'r'),
        ("ExecutionReconciliationAssessments", "UserId", "Users", 'r'),
        ("ExecutionReconciliationAssessments", "ConversationId", "Conversations", 'r'),
        ("ExecutionReconciliationAssessments", "ProjectId", "Projects", 'r'),
        ("ExecutionReconciliationAssessments", "ApprovalId", "ActionApprovals", 'r'),
        ("ExecutionVerificationAssessments", "ActionId", "ActionExecutionStates", 'r'),
        ("ExecutionVerificationAssessments", "UserId", "Users", 'r'),
        ("ExecutionVerificationAssessments", "ConversationId", "Conversations", 'r'),
        ("ExecutionVerificationAssessments", "ProjectId", "Projects", 'r'),
        ("ExecutionVerificationAssessments", "ApprovalId", "ActionApprovals", 'r')
    ];

    // Exact names plus uniqueness, columns and selected predicates from EF/migrations.
    internal static readonly (string Table, string Name, string Columns, string? Predicate)[] UniqueIndexes =
    [
        ("Users", "IX_Users_AuthUserId", "authuserid", null),
        ("ToolPermissionGrants", "UX_ToolPermissionGrants_ActiveExactGrant",
            "userid,conversationid,projectid,toolid,requiredpermission", "status=1andrevokedatisnull"),
        ("ActionExecutionStates", "IX_ActionExecutionStates_ApprovalId", "approvalid", null),
        ("ActionExecutionStates", "IX_ActionExecutionStates_ExecutionId", "executionid", "executionidisnotnull"),
        ("SecurityAuditOutbox", "IX_SecurityAuditOutbox_AuditEventId", "auditeventid", null)
    ];

    internal static readonly (string Table, string Name, string[] Markers)[] CheckConstraints =
    [
        ("Projects", "CK_Projects_Version_Positive", ["version>0"]),
        ("ToolPermissionGrants", "CK_ToolPermissionGrants_NonemptyIds", ["userid", "projectid"]),
        ("ToolPermissionGrants", "CK_ToolPermissionGrants_ToolId", ["toolid"]),
        ("ToolPermissionGrants", "CK_ToolPermissionGrants_RequiredPermission", ["requiredpermission", "0", "1", "2"]),
        ("ToolPermissionGrants", "CK_ToolPermissionGrants_Status", ["status=1", "status=2", "revokedatisnull", "revokedatisnotnull"]),
        ("ActionExecutionStates", "CK_ActionExecutionStates_NonemptyIds", ["actionid", "approvalid"]),
        ("ActionExecutionStates", "CK_ActionExecutionStates_ToolId", ["toolid"]),
        ("ActionExecutionStates", "CK_ActionExecutionStates_State", ["status=0", "status=1", "version=0", "version=1", "version=2", "approvalconsumed", "reservedat", "completedat"]),
        ("SecurityAuditEvents", "CK_SecurityAuditEvents_Ids", ["userid", "projectid"]),
        ("SecurityAuditEvents", "CK_SecurityAuditEvents_ToolId", ["toolid"]),
        ("SecurityAuditEvents", "CK_SecurityAuditEvents_Enums", ["eventtype", "outcome", "0", "11", "3"]),
        ("SecurityAuditOutbox", "CK_SecurityAuditOutbox_Ids", ["auditeventid", "executionid"]),
        ("SecurityAuditOutbox", "CK_SecurityAuditOutbox_ToolId", ["toolid"]),
        ("SecurityAuditOutbox", "CK_SecurityAuditOutbox_Enums", ["deliverystate", "eventtype", "outcome", "0", "11", "3", "2"]),
        ("SecurityAuditOutbox", "CK_SecurityAuditOutbox_Delivery", ["deliverystate", "attemptcount", "deliveredat"]),
        ("ExecutionReconciliationAssessments", "CK_ExecutionReconciliationAssessments_Ids", ["actionid", "approvalid"]),
        ("ExecutionReconciliationAssessments", "CK_ExecutionReconciliationAssessments_ToolId", ["toolid"]),
        ("ExecutionReconciliationAssessments", "CK_ExecutionReconciliationAssessments_State", ["observedstatus=1", "observedversion=1", "reason", "disposition", "0", "5"]),
        ("ExecutionVerificationAssessments", "CK_ExecutionVerificationAssessments_Ids", ["actionid", "approvalid"]),
        ("ExecutionVerificationAssessments", "CK_ExecutionVerificationAssessments_ToolId", ["toolid"]),
        ("ExecutionVerificationAssessments", "CK_ExecutionVerificationAssessments_State", ["executionstatus", "outcome", "executionversionisnull", "executionversion=2", "proofsource=0", "proofversion=0", "proofsource=1", "proofversion=1", "2", "3"])
    ];
}
