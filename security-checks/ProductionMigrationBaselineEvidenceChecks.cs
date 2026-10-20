using Aura.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

internal static class ProductionMigrationBaselineEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var reads = new List<string>();
        var calls = 0;
        Task<ProductionMigrationBaselineEvidence.Stage> Pass(string _, CancellationToken __)
        {
            calls++;
            return Task.FromResult(ProductionMigrationBaselineEvidence.Stage.Passed);
        }
        string? Absent(string key)
        {
            reads.Add(key);
            return key == ProductionMigrationBaselineEvidence.EnableVariable ? null :
                throw new InvalidOperationException("Unexpected environment read.");
        }
        check(await ProductionMigrationBaselineEvidence.RunAsync(Absent, output.Add, Pass) == 0 &&
            output.SequenceEqual(["PRODUCTION MIGRATION BASELINE: SKIPPED"]) && calls == 0 &&
            reads.SequenceEqual([ProductionMigrationBaselineEvidence.EnableVariable]),
            "15B missing opt-in skips before connection input or DB runner");

        output.Clear();
        reads.Clear();
        string? NoDisposable(string key)
        {
            reads.Add(key);
            return key == ProductionMigrationBaselineEvidence.EnableVariable ? "true" :
                key == ProductionMigrationBaselineEvidence.DisposableVariable ? null :
                throw new InvalidOperationException("Connection read before acknowledgement.");
        }
        check(await ProductionMigrationBaselineEvidence.RunAsync(NoDisposable, output.Add, Pass) == 0 &&
            output.SequenceEqual(["PRODUCTION MIGRATION BASELINE: SKIPPED"]) && calls == 0 &&
            reads.SequenceEqual([ProductionMigrationBaselineEvidence.EnableVariable,
                ProductionMigrationBaselineEvidence.DisposableVariable]),
            "15B missing disposable acknowledgement skips before connection or DB runner");

        const string secret = "Host=example.invalid;Database=aura_disposable_test;Username=test;Password=secret-value";
        var variables = new Dictionary<string, string?>
        {
            [ProductionMigrationBaselineEvidence.EnableVariable] = "true",
            [ProductionMigrationBaselineEvidence.DisposableVariable] = "true",
            [ProductionMigrationBaselineEvidence.ConnectionVariable] = secret
        };
        string? Read(string key) { reads.Add(key); return variables.GetValueOrDefault(key); }
        foreach (var invalid in new[] { "broken", "Host=example;Database=production;Username=test",
            "Host=example;Database=aura_disposable_;Username=test", "Host=example;Database=aura_disposable_test" })
        {
            variables[ProductionMigrationBaselineEvidence.ConnectionVariable] = invalid;
            output.Clear();
            check(await ProductionMigrationBaselineEvidence.RunAsync(Read, output.Add, Pass) == 1 &&
                output.SequenceEqual(["INPUT: FAIL", "RESULT: FAIL"]) && calls == 0,
                "15B malformed or non-disposable connection fails before DB runner");
        }
        variables[ProductionMigrationBaselineEvidence.ConnectionVariable] = secret;
        output.Clear();
        check(await ProductionMigrationBaselineEvidence.RunAsync(Read, output.Add, Pass) == 0 &&
            calls == 1 && output.Count == 7 && output.Last() == "RESULT: PASS" &&
            output.All(line => !line.Contains("secret-value", StringComparison.Ordinal)),
            "15B explicit command emits bounded PASS categories without secrets");
        foreach (var stage in Enum.GetValues<ProductionMigrationBaselineEvidence.Stage>()
            .Where(stage => stage != ProductionMigrationBaselineEvidence.Stage.Passed))
        {
            output.Clear();
            check(await ProductionMigrationBaselineEvidence.RunAsync(Read, output.Add,
                    (_, _) => Task.FromResult(stage)) == 1 &&
                output.Last() == "RESULT: FAIL" &&
                output.All(line => !line.Contains("secret-value", StringComparison.Ordinal)),
                "15B bounded failure category cannot produce PASS or disclose credentials");
        }
        output.Clear();
        check(await ProductionMigrationBaselineEvidence.RunAsync(Read, output.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15B raw PostgreSQL exception is suppressed");

        var baseline = SyntheticBaseline();
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline) ==
            ProductionMigrationBaselineEvidence.Stage.Passed,
            "15B complete synthetic catalog satisfies exact source-derived manifest");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Columns = baseline.Columns.Select(x => x.Table == "Projects" && x.Name == "Version"
                ? x with { DefaultValue = null } : x).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleColumn &&
            ProductionMigrationBaselineEvidence.Evaluate(baseline with
            {
                Triggers = []
            }) == ProductionMigrationBaselineEvidence.Stage.MissingTrigger &&
            ProductionMigrationBaselineEvidence.Evaluate(baseline with
            {
                Constraints = baseline.Constraints.Where(x =>
                    x.Name != "CK_Projects_Version_Positive").ToArray()
            }) == ProductionMigrationBaselineEvidence.Stage.MissingCheckConstraint,
            "17T baseline rejects missing version default, trigger, or positive constraint");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with { HistoryExists = false }) ==
            ProductionMigrationBaselineEvidence.Stage.MissingHistory,
            "15B absent migration history table fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            History = [.. baseline.History, baseline.History[0]]
        }) == ProductionMigrationBaselineEvidence.Stage.DuplicateMigration,
            "15B duplicate migration ID fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            History = baseline.History.Skip(1).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.MissingMigration,
            "15B missing expected migration fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            History = [.. baseline.History, "20261015000000_UnknownFuture"]
        }) == ProductionMigrationBaselineEvidence.Stage.UnexpectedMigration,
            "15B unexpected future migration fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            History = baseline.History.Reverse().ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.MigrationOrder,
            "15B history sequence differing from repository order fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Tables = baseline.Tables.Where(x => x != "SecurityAuditOutbox").ToHashSet()
        }) == ProductionMigrationBaselineEvidence.Stage.MissingTable,
            "15B missing required security table fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Columns = baseline.Columns.Where(x => x.Name != "AuthUserId").ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.MissingColumn,
            "15B missing critical identity column fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Columns = baseline.Columns.Select(x => x.Table == "Users" && x.Name == "AuthUserId"
                ? x with { Type = "text" } : x).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleColumn,
            "15B incompatible critical column type fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Constraints = baseline.Constraints.Where(x => x.Name != "PK_Users").ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.MissingPrimaryKey,
            "15B missing primary key fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Constraints = baseline.Constraints.Where(x =>
                x.Name != "FK_ToolPermissionGrants_Users_UserId").ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.MissingForeignKey,
            "15B missing critical foreign key fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Indexes = baseline.Indexes.Where(x =>
                x.Name != "UX_ToolPermissionGrants_ActiveExactGrant").ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.MissingUniqueIndex,
            "15B missing active exact-grant unique index fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Constraints = baseline.Constraints.Where(x =>
                x.Name != "CK_SecurityAuditOutbox_Delivery").ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.MissingCheckConstraint,
            "15B missing delivery-state check constraint fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Constraints = baseline.Constraints.Select(x => x.Name == "CK_SecurityAuditOutbox_Delivery"
                ? x with { Definition = "CHECK (true)" } : x).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleConstraint,
            "15B weakened check constraint is incompatible");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Constraints = baseline.Constraints.Select(x => x.Name == "CK_SecurityAuditOutbox_Delivery"
                ? x with { Definition = x.Definition.Replace(
                    "\"AttemptCount\" > 0 AND \"DeliveredAt\" IS NULL)",
                    "\"AttemptCount\" > 0)", StringComparison.Ordinal) } : x).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleConstraint,
            "15B pre-hardening Failed delivery expression cannot pass");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Columns = baseline.Columns.Where(x => x.Name != "ExecutionVersion" ||
                x.Table != "ExecutionVerificationAssessments").ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.MissingColumn,
            "17O missing verification execution-version column fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Columns = baseline.Columns.Select(x => x.Table == "ExecutionVerificationAssessments" &&
                x.Name == "ExecutionVersion" ? x with { Required = true } : x).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleColumn,
            "17O nonnullable historical execution-version column fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Constraints = baseline.Constraints.Select(x =>
                x.Name == "CK_ExecutionVerificationAssessments_State"
                    ? x with { Definition = x.Definition.Replace("\"ExecutionVersion\" = 2",
                        "\"ExecutionVersion\" = 3", StringComparison.Ordinal) } : x).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleConstraint,
            "17O wrong terminal execution version fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Constraints = baseline.Constraints.Select(x =>
                x.Name == "CK_ExecutionVerificationAssessments_State"
                    ? x with { Definition = x.Definition.Replace("\"ProofSource\" = 1 AND \"ProofVersion\" = 1",
                        "\"ProofSource\" = 1 AND \"ProofVersion\" = 0", StringComparison.Ordinal) } : x).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleConstraint,
            "17O mismatched proof source/version pair fails");
        check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
        {
            Constraints = baseline.Constraints.Select(x =>
                x.Name == "CK_ExecutionVerificationAssessments_State"
                    ? x with { Definition = "CHECK (" +
                        ProductionMigrationBaselineEvidence.VerificationFoundationExpression + ")" }
                    : x).ToArray()
        }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleConstraint,
            "17Q pre-Verified sixth constraint cannot pass seven-ID schema baseline");
        foreach (var (oldText, newText) in new[]
        {
            ("\"ExecutionStatus\" = 2 AND \"Outcome\" = 0", "\"ExecutionStatus\" = 3 AND \"Outcome\" = 0"),
            ("\"Outcome\" = 0 AND \"ExecutionVersion\" = 2", "\"Outcome\" = 0 AND \"ExecutionVersion\" = 1"),
            ("\"ProofSource\" = 1 AND \"ProofVersion\" = 1)", "\"ProofSource\" = 2 AND \"ProofVersion\" = 1)")
        })
            check(ProductionMigrationBaselineEvidence.Evaluate(baseline with
            {
                Constraints = baseline.Constraints.Select(x =>
                    x.Name == "CK_ExecutionVerificationAssessments_State"
                        ? x with { Definition = x.Definition.Replace(oldText, newText,
                            StringComparison.Ordinal) } : x).ToArray()
            }) == ProductionMigrationBaselineEvidence.Stage.IncompatibleConstraint,
                "17Q wrong Verified status, execution version or proof source fails");

        var root = Directory.GetCurrentDirectory();
        var harness = File.ReadAllText(Path.Combine(root, "security-checks",
            "ProductionMigrationBaselineEvidence.cs"));
        var program = File.ReadAllText(Path.Combine(root, "security-checks", "Program.cs"));
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        var files = Directory.GetFiles(Path.Combine(root, "Aura.Infrastructure", "Data", "Migrations"),
            "*.cs").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        check(files.SequenceEqual(ProductionMigrationBaselineEvidence.ExpectedMigrations),
            "17Q seven expected migration IDs match exact repository source order");
        check(program.Contains("args[0] == ProductionMigrationBaselineEvidence.Command", StringComparison.Ordinal) &&
            program.IndexOf("await ProductionMigrationBaselineEvidenceChecks.RunAsync(Check)", StringComparison.Ordinal) >
                program.IndexOf("var checks = 0", StringComparison.Ordinal) &&
            !api.Contains("ProductionMigrationBaselineEvidence", StringComparison.Ordinal),
            "15B explicit live command is separate from normal offline suite and API startup");
        check(!harness.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !harness.Contains("EnsureCreated(", StringComparison.Ordinal) &&
            !harness.Contains("ExecuteNonQuery", StringComparison.Ordinal) &&
            !harness.Contains("SaveChangesAsync(", StringComparison.Ordinal) &&
            !harness.Contains("AddHostedService", StringComparison.Ordinal) &&
            !harness.Contains("Task.Delay(", StringComparison.Ordinal) &&
            !harness.Contains("while (true)", StringComparison.Ordinal) &&
            !harness.Contains("TRUNCATE ", StringComparison.OrdinalIgnoreCase),
            "15B harness has no migration apply, DDL, DML, worker, polling or retry path");
        check(!Regex.IsMatch(harness,
                @"\b(CREATE|DROP|ALTER|INSERT|UPDATE|DELETE|TRUNCATE)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            harness.Contains("SELECT ", StringComparison.Ordinal) &&
            harness.Contains("__EFMigrationsHistory", StringComparison.Ordinal),
            "15B catalog command text is read-only and migration history is never written");
        var composition = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        check(!composition.CompositionComplete && !composition.Ownership &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            api.Contains(".AddOwnershipRuntimeFoundation(", StringComparison.Ordinal),
            "15B live PASS cannot mark composition ready or activate production runtime");
    }

    private static ProductionMigrationBaselineEvidence.Snapshot SyntheticBaseline()
    {
        var constraints = new List<ProductionMigrationBaselineEvidence.Constraint>();
        foreach (var (table, key) in ProductionMigrationBaselineEvidence.PrimaryKeys)
            constraints.Add(new(table, "PK_" + table, 'p', null, ' ', true,
                "PRIMARY KEY (" + key + ")"));
        foreach (var (table, column, target, action) in ProductionMigrationBaselineEvidence.ForeignKeys)
        {
            var name = "FK_" + table + "_" + target + "_" + column;
            if (name.Length > 63) name = name[..63];
            constraints.Add(new(table, name,
                'f', target, action, true, "FOREIGN KEY (" + column + ") REFERENCES " + target));
        }
        foreach (var (table, name, markers) in ProductionMigrationBaselineEvidence.CheckConstraints)
            constraints.Add(new(table, name, 'c', null, ' ', true,
                name == "CK_SecurityAuditOutbox_Delivery"
                    ? "CHECK (" + ProductionMigrationBaselineEvidence.HardenedDeliveryExpression + ")"
                    : name == "CK_ExecutionVerificationAssessments_State"
                        ? "CHECK (" + ProductionMigrationBaselineEvidence.IndependentVerificationOutcomeExpression + ")"
                        : "CHECK (" + string.Join(" AND ", markers) + ")"));
        var indexes = ProductionMigrationBaselineEvidence.UniqueIndexes.Select(item =>
            new ProductionMigrationBaselineEvidence.Index(item.Table, item.Name, true,
                "CREATE UNIQUE INDEX " + item.Name + " ON " + item.Table + " (" +
                item.Columns + ") WHERE " + item.Predicate)).ToArray();
        return new(true, ProductionMigrationBaselineEvidence.ExpectedMigrations,
            ProductionMigrationBaselineEvidence.RequiredTables.ToHashSet(StringComparer.Ordinal),
            ProductionMigrationBaselineEvidence.RequiredColumns, constraints, indexes,
            [new("TR_Projects_IncrementVersion",
                "CREATE TRIGGER TR_Projects_IncrementVersion BEFORE INSERT OR UPDATE ON Projects " +
                "EXECUTE FUNCTION aura_increment_project_version()",
                "IF NEW.Version IS DISTINCT FROM 1 THEN RAISE EXCEPTION; END IF; " +
                "IF NEW.Version IS DISTINCT FROM OLD.Version THEN RAISE EXCEPTION; " +
                "END IF; IF OLD.Version = 9223372036854775807 THEN RAISE EXCEPTION; " +
                "END IF; NEW.Version := OLD.Version + 1;")]);
    }
}
