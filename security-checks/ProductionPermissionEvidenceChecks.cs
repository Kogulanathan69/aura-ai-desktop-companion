using System.Reflection;
using System.Text.RegularExpressions;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionPermissionEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var reads = new List<string>();
        var calls = 0;
        Task<LivePermissionStage> Pass(LivePermissionInput _, CancellationToken __)
        { calls++; return Task.FromResult(LivePermissionStage.Passed); }
        string? NoOptIn(string key)
        {
            reads.Add(key);
            return key == LivePermissionVerification.ProductionEnableVariable ? null :
                throw new InvalidOperationException("Unexpected input access.");
        }
        check(await LivePermissionVerification.RunProductionAsync(NoOptIn, output.Add, Pass) == 0 &&
            output.SequenceEqual(["PRODUCTION PERMISSION EVIDENCE: SKIPPED"]) && calls == 0 &&
            reads.SequenceEqual([LivePermissionVerification.ProductionEnableVariable]),
            "15C absent opt-in skips before disposable and connection access");
        output.Clear(); reads.Clear();
        string? NoDisposable(string key)
        {
            reads.Add(key);
            return key == LivePermissionVerification.ProductionEnableVariable ? "true" :
                key == LivePermissionVerification.DisposableVariable ? null :
                throw new InvalidOperationException("Connection accessed before acknowledgement.");
        }
        check(await LivePermissionVerification.RunProductionAsync(NoDisposable, output.Add, Pass) == 0 &&
            output.SequenceEqual(["PRODUCTION PERMISSION EVIDENCE: SKIPPED"]) && calls == 0 &&
            reads.SequenceEqual([LivePermissionVerification.ProductionEnableVariable,
                LivePermissionVerification.DisposableVariable]),
            "15C absent disposable acknowledgement skips before connection access");

        var user = Guid.NewGuid(); var conversation = Guid.NewGuid();
        var project = Guid.NewGuid(); var grant = Guid.NewGuid(); var revoked = Guid.NewGuid();
        const string secret = "Host=example.invalid;Database=aura_disposable_test;Username=test;Password=private-value";
        var variables = new Dictionary<string, string?>
        {
            [LivePermissionVerification.ProductionEnableVariable] = "true",
            [LivePermissionVerification.DisposableVariable] = "true",
            ["AURA_VERIFY_PERMISSION_USER_ID"] = user.ToString("D"),
            ["AURA_VERIFY_PERMISSION_CONVERSATION_ID"] = conversation.ToString("D"),
            ["AURA_VERIFY_PERMISSION_PROJECT_ID"] = project.ToString("D"),
            ["AURA_VERIFY_PERMISSION_TOOL_ID"] = "check-tool",
            ["AURA_VERIFY_PERMISSION_REQUIREMENT"] = "OwnedProjectRead",
            ["AURA_VERIFY_PERMISSION_GRANT_ID"] = grant.ToString("D"),
            ["AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID"] = revoked.ToString("D"),
            ["ConnectionStrings__DefaultConnection"] = secret
        };
        string? Read(string key) { reads.Add(key); return variables.GetValueOrDefault(key); }
        foreach (var (key, value) in new[]
        {
            ("AURA_VERIFY_PERMISSION_USER_ID", "bad-guid"),
            ("AURA_VERIFY_PERMISSION_CONVERSATION_ID", Guid.Empty.ToString("D")),
            ("AURA_VERIFY_PERMISSION_PROJECT_ID", "bad-guid"),
            ("AURA_VERIFY_PERMISSION_GRANT_ID", "bad-guid"),
            ("AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID", "bad-guid"),
            ("AURA_VERIFY_PERMISSION_TOOL_ID", "Bad-Tool"),
            ("AURA_VERIFY_PERMISSION_TOOL_ID", "bad*tool"),
            ("AURA_VERIFY_PERMISSION_REQUIREMENT", "999")
        })
        {
            var saved = variables[key]; variables[key] = value;
            output.Clear(); reads.Clear();
            check(await LivePermissionVerification.RunProductionAsync(Read, output.Add, Pass) == 1 &&
                output.SequenceEqual(["INPUT: FAIL", "RESULT: FAIL"]) && calls == 0 &&
                !reads.Contains("ConnectionStrings__DefaultConnection"),
                "15C malformed GUID, noncanonical tool or permission fails before DB input");
            variables[key] = saved;
        }
        foreach (var invalid in new[] { "bad=keyword",
            "Host=example.invalid;Database=production;Username=test",
            "Host=example.invalid;Database=aura_disposable_test" })
        {
            variables["ConnectionStrings__DefaultConnection"] = invalid; output.Clear();
            check(await LivePermissionVerification.RunProductionAsync(Read, output.Add, Pass) == 1 &&
                output.SequenceEqual(["INPUT: FAIL", "RESULT: FAIL"]) && calls == 0,
                "15C malformed or non-disposable connection fails before DB runner");
        }
        variables["ConnectionStrings__DefaultConnection"] = secret;
        output.Clear();
        check(await LivePermissionVerification.RunProductionAsync(Read, output.Add, Pass) == 0 &&
            calls == 1 && output.Count == 7 && output.Last() == "RESULT: PASS" &&
            output.All(x => !x.Contains("private-value", StringComparison.Ordinal)),
            "15C valid gated input emits bounded categories without secrets");
        variables["AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID"] = null;
        output.Clear();
        check(await LivePermissionVerification.RunProductionAsync(Read, output.Add, Pass) == 2 &&
            output.SequenceEqual(["REVOKED GRANT CHECK: NOT PROVEN", "RESULT: INCOMPLETE"]),
            "15C missing revoked fixture cannot pass even if runner reports success");
        variables["AURA_VERIFY_REVOKED_PERMISSION_GRANT_ID"] = revoked.ToString("D");
        output.Clear();
        check(await LivePermissionVerification.RunProductionAsync(Read, output.Add,
                (_, _) => Task.FromResult(LivePermissionStage.RevokedNotProven)) == 2 &&
            output.SequenceEqual(["REVOKED GRANT CHECK: NOT PROVEN", "RESULT: INCOMPLETE"]),
            "15C missing revoked fixture cannot produce overall PASS");
        foreach (var stage in Enum.GetValues<LivePermissionStage>()
            .Where(x => x is not (LivePermissionStage.Passed or LivePermissionStage.RevokedNotProven)))
        {
            output.Clear();
            check(await LivePermissionVerification.RunProductionAsync(Read, output.Add,
                    (_, _) => Task.FromResult(stage)) == 1 &&
                output.Last() == "RESULT: FAIL" &&
                output.All(x => !x.Contains("private-value", StringComparison.Ordinal)),
                "15C bounded failure stage cannot disclose secrets or pass");
        }
        output.Clear();
        check(await LivePermissionVerification.RunProductionAsync(Read, output.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15C raw DB exceptions and credentials are suppressed");

        ToolIdentifier.TryCreate("check-tool", out var tool);
        var owner = new User { Id = user, IsActive = true };
        var ownedConversation = new Conversation { Id = conversation, UserId = user,
            ProjectId = project, Type = "Project" };
        var ownedProject = new Project { Id = project, UserId = user };
        var activeRow = new ToolPermissionGrantRecord { Id = grant, UserId = user,
            ConversationId = conversation, ProjectId = project, ToolId = tool!.Value,
            RequiredPermission = 0, Status = ToolPermissionGrantStatus.Active };
        var revokedRow = new ToolPermissionGrantRecord { Id = revoked, UserId = user,
            ConversationId = conversation, ProjectId = project, ToolId = "revoked-tool",
            RequiredPermission = 0, Status = ToolPermissionGrantStatus.Revoked,
            RevokedAt = DateTime.UtcNow };
        var rows = new List<ToolPermissionGrantRecord> { activeRow, revokedRow };
        var db = DispatchProxy.Create<IAuraDbContext,
            ToolPermissionPersistenceChecks.PermissionContextProxy>();
        var proxy = (ToolPermissionPersistenceChecks.PermissionContextProxy)(object)db;
        proxy.Grants = new CheckDbSet<ToolPermissionGrantRecord>(rows);
        proxy.Users = new CheckDbSet<User>([owner]);
        proxy.Conversations = new CheckDbSet<Conversation>([ownedConversation]);
        proxy.Projects = new CheckDbSet<Project>([ownedProject]);
        var input = new LivePermissionInput(secret, user, conversation, project, tool,
            ToolPermissionRequirement.OwnedProjectRead, grant, revoked);
        check(await LivePermissionVerification.VerifyProductionSourceAsync(db, input, default) ==
            LivePermissionStage.Passed && proxy.Saves == 0,
            "15C exact active grant and existing revoked fixture pass read-only evidence");
        check(await LivePermissionVerification.VerifyProductionSourceAsync(db,
                input with { RevokedGrantId = null }, default) == LivePermissionStage.RevokedNotProven,
            "15C no revoked fixture yields incomplete evidence despite active grant");
        revokedRow.Status = ToolPermissionGrantStatus.Active;
        check(await LivePermissionVerification.VerifyProductionSourceAsync(db, input, default) ==
            LivePermissionStage.RevokedFixtureFailed,
            "15C purported revoked fixture must actually be revoked");
        revokedRow.Status = ToolPermissionGrantStatus.Revoked;
        owner.IsActive = false;
        check(await LivePermissionVerification.VerifyProductionSourceAsync(db, input, default) ==
            LivePermissionStage.NoTestGrant && proxy.Saves == 0,
            "15C inactive user cannot establish positive permission evidence");
        owner.IsActive = true;
        var source = new ToolPermissionPersistenceSource(db);
        var request = new ToolPermissionValidationRequest(new ActionScope(user, conversation, project),
            tool, ToolPermissionRequirement.OwnedProjectRead);
        foreach (var negative in new[]
        {
            request with { Scope = request.Scope with { UserId = Guid.NewGuid() } },
            request with { Scope = request.Scope with { ConversationId = Guid.NewGuid() } },
            request with { Scope = request.Scope with { ProjectId = Guid.NewGuid() } },
            request with { Requirement = ToolPermissionRequirement.OwnedProjectWrite }
        })
            check((await source.GetAsync(negative, default)).Status == ToolPermissionLookupStatus.NotFound,
                "15C wrong user, conversation, project or permission is not found");
        ToolIdentifier.TryCreate("other-tool", out var otherTool);
        check((await source.GetAsync(request with { ToolId = otherTool! }, default)).Status ==
            ToolPermissionLookupStatus.NotFound,
            "15C wrong canonical ToolId is not found");

        var root = Directory.GetCurrentDirectory();
        var harness = File.ReadAllText(Path.Combine(root, "security-checks", "LivePermissionVerification.cs"));
        var program = File.ReadAllText(Path.Combine(root, "security-checks", "Program.cs"));
        var api = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        check(program.Contains("args[0] == LivePermissionVerification.ProductionCommand", StringComparison.Ordinal) &&
            program.IndexOf("await ProductionPermissionEvidenceChecks.RunAsync(Check)", StringComparison.Ordinal) >
                program.IndexOf("var checks = 0", StringComparison.Ordinal) &&
            !api.Contains("LivePermissionVerification", StringComparison.Ordinal),
            "15C explicit live command is separate from normal offline suite and API startup");
        check(!harness.Contains("SaveChangesAsync(", StringComparison.Ordinal) &&
            !harness.Contains("ExecuteUpdateAsync(", StringComparison.Ordinal) &&
            !harness.Contains("ExecuteDeleteAsync(", StringComparison.Ordinal) &&
            !harness.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !harness.Contains("EnsureCreated(", StringComparison.Ordinal) &&
            !harness.Contains("AddHostedService", StringComparison.Ordinal) &&
            !harness.Contains("Task.Delay(", StringComparison.Ordinal) &&
            !harness.Contains("while (true)", StringComparison.Ordinal) &&
            !Regex.IsMatch(harness, @"\b(INSERT|UPDATE|DELETE|TRUNCATE|CREATE|DROP|ALTER)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "15C live harness has no write, migration, schema mutation, worker or retry path");
        var snapshot = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        check(!snapshot.Permission && !snapshot.CompositionComplete &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<IToolPermissionSource", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal),
            "15C live PASS cannot register production permission or activate execution");
    }
}
