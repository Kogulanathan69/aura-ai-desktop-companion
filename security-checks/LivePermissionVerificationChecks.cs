using System.Reflection;
using Aura.Application.Common.Interfaces;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;

internal static class LivePermissionVerificationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var lines = new List<string>();
        var calls = 0;
        Task<LivePermissionStage> Fake(LivePermissionInput _, CancellationToken __)
        { calls++; return Task.FromResult(LivePermissionStage.Passed); }
        string? Absent(string key) => key == LivePermissionVerification.EnableVariable
            ? null : throw new InvalidOperationException("Unexpected environment read.");
        check(await LivePermissionVerification.RunAsync(Absent, lines.Add, Fake) == 0 &&
            lines.SequenceEqual(["LIVE PERMISSION VERIFY: SKIPPED"]) && calls == 0,
            "13B live mode without opt-in skips before inputs or DB runner");
        lines.Clear();
        check(await LivePermissionVerification.RunAsync(
                key => key == LivePermissionVerification.EnableVariable ? "false" : null,
                lines.Add, Fake) == 0 && calls == 0 &&
            lines.SequenceEqual(["LIVE PERMISSION VERIFY: SKIPPED"]),
            "13B only exact true opt-in can start live permission verification");

        var userId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        const string secret = "Host=example;Database=test;Password=synthetic-secret";
        var variables = new Dictionary<string, string?> {
            [LivePermissionVerification.EnableVariable] = "true",
            ["ConnectionStrings__DefaultConnection"] = secret,
            ["AURA_VERIFY_PERMISSION_USER_ID"] = userId.ToString("D"),
            ["AURA_VERIFY_PERMISSION_CONVERSATION_ID"] = conversationId.ToString("D"),
            ["AURA_VERIFY_PERMISSION_PROJECT_ID"] = projectId.ToString("D"),
            ["AURA_VERIFY_PERMISSION_TOOL_ID"] = "check-tool",
            ["AURA_VERIFY_PERMISSION_REQUIREMENT"] = "OwnedProjectRead",
            ["AURA_VERIFY_PERMISSION_GRANT_ID"] = grantId.ToString("D")
        };
        string? Read(string key) => variables.GetValueOrDefault(key);
        foreach (var key in new[] { "ConnectionStrings__DefaultConnection",
            "AURA_VERIFY_PERMISSION_USER_ID", "AURA_VERIFY_PERMISSION_CONVERSATION_ID",
            "AURA_VERIFY_PERMISSION_PROJECT_ID", "AURA_VERIFY_PERMISSION_TOOL_ID",
            "AURA_VERIFY_PERMISSION_REQUIREMENT" })
        {
            var saved = variables[key];
            variables[key] = null;
            lines.Clear();
            check(await LivePermissionVerification.RunAsync(Read, lines.Add, Fake) == 1 &&
                lines.SequenceEqual(["LIVE PERMISSION VERIFY: INVALID INPUT"]) && calls == 0,
                "13B missing required input fails before DB runner: " + key);
            variables[key] = saved;
        }
        foreach (var (key, bad) in new[] {
            ("ConnectionStrings__DefaultConnection", "malformed"),
            ("AURA_VERIFY_PERMISSION_USER_ID", "invalid"),
            ("AURA_VERIFY_PERMISSION_CONVERSATION_ID", Guid.Empty.ToString("D")),
            ("AURA_VERIFY_PERMISSION_PROJECT_ID", "invalid"),
            ("AURA_VERIFY_PERMISSION_TOOL_ID", "bad*tool"),
            ("AURA_VERIFY_PERMISSION_REQUIREMENT", "999"),
            ("AURA_VERIFY_PERMISSION_GRANT_ID", Guid.Empty.ToString("D")) })
        {
            var saved = variables[key];
            variables[key] = bad;
            lines.Clear();
            check(await LivePermissionVerification.RunAsync(Read, lines.Add, Fake) == 1 &&
                lines.SequenceEqual(["LIVE PERMISSION VERIFY: INVALID INPUT"]) && calls == 0,
                "13B malformed input fails before DB runner: " + key);
            variables[key] = saved;
        }
        LivePermissionInput? observed = null;
        async Task<LivePermissionStage> Observe(LivePermissionInput input, CancellationToken token)
        { observed = input; return await Fake(input, token); }
        lines.Clear();
        check(await LivePermissionVerification.RunAsync(Read, lines.Add, Observe) == 0 &&
            calls == 1 && observed?.UserId == userId &&
            observed.ConversationId == conversationId && observed.ProjectId == projectId &&
            observed.ToolId.Value == "check-tool" && observed.GrantId == grantId &&
            lines.SequenceEqual(["DB CONNECTIVITY: PASS", "SCHEMA CHECK: PASS",
                "PERMISSION LOOKUP: PASS", "NEGATIVE CHECKS: PASS", "RESULT: PASS"]) &&
            lines.All(line => !line.Contains(secret, StringComparison.Ordinal)),
            "13B opted-in runner receives bounded inputs and emits fixed categories");
        variables["AURA_VERIFY_PERMISSION_GRANT_ID"] = null;
        lines.Clear();
        check(await LivePermissionVerification.RunAsync(Read, lines.Add, Observe) == 0 &&
            observed?.GrantId is null && calls == 2,
            "13B expected grant ID is optional but never invented");
        foreach (var stage in Enum.GetValues<LivePermissionStage>()
            .Where(value => value != LivePermissionStage.Passed))
        {
            lines.Clear();
            check(await LivePermissionVerification.RunAsync(Read, lines.Add,
                    (_, _) => Task.FromResult(stage)) == 1 && lines.Count == 1 &&
                lines[0] is "DB CONNECTIVITY: FAIL" or "SCHEMA CHECK: FAIL" or
                    "PERMISSION LOOKUP: NO TEST GRANT" or "PERMISSION LOOKUP: FAIL" or
                    "NEGATIVE CHECKS: FAIL" or "RESULT: FAIL" &&
                !lines[0].Contains(secret, StringComparison.Ordinal),
                "13B live failure emits only safe bounded category");
        }
        lines.Clear();
        check(await LivePermissionVerification.RunAsync(Read, lines.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            lines.SequenceEqual(["RESULT: FAIL"]),
            "13B live raw DB/secret exception never reaches output");

        ToolIdentifier.TryCreate("check-tool", out var toolId);
        var grant = new ToolPermissionGrantRecord { Id = grantId, UserId = userId,
            ConversationId = conversationId, ProjectId = projectId, ToolId = toolId!.Value,
            RequiredPermission = 0, Status = ToolPermissionGrantStatus.Active };
        var grants = new List<ToolPermissionGrantRecord> { grant };
        var db = DispatchProxy.Create<IAuraDbContext,
            ToolPermissionPersistenceChecks.PermissionContextProxy>();
        var proxy = (ToolPermissionPersistenceChecks.PermissionContextProxy)(object)db;
        proxy.Grants = new CheckDbSet<ToolPermissionGrantRecord>(grants);
        proxy.Users = new CheckDbSet<User>([new User { Id = userId, IsActive = true }]);
        proxy.Conversations = new CheckDbSet<Conversation>([new Conversation { Id = conversationId,
            UserId = userId, ProjectId = projectId, Type = "Project" }]);
        proxy.Projects = new CheckDbSet<Project>([new Project { Id = projectId, UserId = userId }]);
        var input = new LivePermissionInput(secret, userId, conversationId, projectId, toolId,
            ToolPermissionRequirement.OwnedProjectRead, grantId);
        check(await LivePermissionVerification.VerifySourceAsync(db, input, default) ==
            LivePermissionStage.Passed && proxy.Saves == 0,
            "13B offline seam verifies exact positive and five negatives read-only");
        check(await LivePermissionVerification.VerifySourceAsync(db,
            input with { GrantId = Guid.NewGuid() }, default) == LivePermissionStage.LookupFailed,
            "13B optional expected grant ID must match exactly");
        grants.Clear();
        check(await LivePermissionVerification.VerifySourceAsync(db, input, default) ==
            LivePermissionStage.NoTestGrant && proxy.Saves == 0,
            "13B no known grant is not reported as successful live verification");
    }
}
