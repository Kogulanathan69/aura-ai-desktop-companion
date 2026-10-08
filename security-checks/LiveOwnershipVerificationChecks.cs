using System.Reflection;
using Aura.Application.Common.Interfaces;
using Aura.Domain.Entities;

internal static class LiveOwnershipVerificationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var calls = 0;
        Task<LiveOwnershipStage> Fake(LiveOwnershipInput _, CancellationToken __)
        {
            calls++;
            return Task.FromResult(LiveOwnershipStage.Passed);
        }
        string? Absent(string key) => key == LiveOwnershipVerification.EnableVariable
            ? null : throw new InvalidOperationException("Unexpected environment read.");
        check(await LiveOwnershipVerification.RunAsync(Absent, output.Add, Fake) == 0 &&
            output.SequenceEqual(["LIVE OWNERSHIP VERIFY: SKIPPED"]) && calls == 0,
            "12F missing opt-in skips before input or DB runner access");
        output.Clear();
        check(await LiveOwnershipVerification.RunAsync(
                key => key == LiveOwnershipVerification.EnableVariable ? "false" : null,
                output.Add, Fake) == 0 && calls == 0 &&
            output.SequenceEqual(["LIVE OWNERSHIP VERIFY: SKIPPED"]),
            "12F explicit true opt-in is required");

        var subject = Guid.NewGuid();
        var internalId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        const string secret = "Host=example;Password=top-secret";
        var variables = new Dictionary<string, string?> {
            [LiveOwnershipVerification.EnableVariable] = "true",
            ["ConnectionStrings__DefaultConnection"] = secret,
            ["AURA_VERIFY_AUTH_USER_ID"] = subject.ToString("D"),
            ["AURA_VERIFY_INTERNAL_USER_ID"] = internalId.ToString("D"),
            ["AURA_VERIFY_CONVERSATION_ID"] = conversationId.ToString("D"),
            ["AURA_VERIFY_PROJECT_ID"] = projectId.ToString("D")
        };
        string? Read(string key) => variables.GetValueOrDefault(key);
        foreach (var key in new[] { "ConnectionStrings__DefaultConnection",
            "AURA_VERIFY_AUTH_USER_ID", "AURA_VERIFY_INTERNAL_USER_ID",
            "AURA_VERIFY_CONVERSATION_ID", "AURA_VERIFY_PROJECT_ID" })
        {
            var saved = variables[key];
            variables[key] = null;
            output.Clear();
            check(await LiveOwnershipVerification.RunAsync(Read, output.Add, Fake) == 1 &&
                output.SequenceEqual(["LIVE OWNERSHIP VERIFY: INVALID INPUT"]) && calls == 0,
                "12F missing required live input fails before DB runner");
            variables[key] = saved;
        }
        foreach (var key in new[] { "AURA_VERIFY_AUTH_USER_ID", "AURA_VERIFY_INTERNAL_USER_ID",
            "AURA_VERIFY_CONVERSATION_ID", "AURA_VERIFY_PROJECT_ID" })
        {
            var saved = variables[key];
            foreach (var invalid in new[] { "not-a-guid", Guid.Empty.ToString("D") })
            {
                variables[key] = invalid;
                output.Clear();
                check(await LiveOwnershipVerification.RunAsync(Read, output.Add, Fake) == 1 &&
                    output.SequenceEqual(["LIVE OWNERSHIP VERIFY: INVALID INPUT"]) && calls == 0,
                    "12F malformed or empty live identity fails before DB runner");
            }
            variables[key] = saved;
        }
        output.Clear();
        LiveOwnershipInput? observed = null;
        async Task<LiveOwnershipStage> Observe(LiveOwnershipInput input, CancellationToken token)
        {
            observed = input;
            return await Fake(input, token);
        }
        check(await LiveOwnershipVerification.RunAsync(Read, output.Add, Observe) == 0 &&
            calls == 1 && observed == new LiveOwnershipInput(secret, subject, internalId,
                conversationId, projectId) && output.Last() == "RESULT: PASS" &&
            output.Count == 6 && output.All(line => !line.Contains(secret, StringComparison.Ordinal) &&
                !line.Contains(subject.ToString("D"), StringComparison.OrdinalIgnoreCase)),
            "12F valid opt-in passes bounded IDs to runner and prints categories only");
        foreach (var stage in Enum.GetValues<LiveOwnershipStage>()
            .Where(stage => stage != LiveOwnershipStage.Passed))
        {
            output.Clear();
            check(await LiveOwnershipVerification.RunAsync(Read, output.Add,
                    (_, _) => Task.FromResult(stage)) == 1 && output.Count == 1 &&
                output[0].EndsWith(": FAIL", StringComparison.Ordinal) &&
                !output[0].Contains(secret, StringComparison.Ordinal),
                "12F live failures emit only fixed safe category");
        }
        output.Clear();
        check(await LiveOwnershipVerification.RunAsync(Read, output.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "12F raw DB or password exception never reaches output");

        var user = new User { Id = internalId, AuthUserId = subject, IsActive = true };
        var conversation = new Conversation { Id = conversationId, UserId = internalId,
            ProjectId = projectId, Type = "Project" };
        var project = new Project { Id = projectId, UserId = internalId };
        var db = DispatchProxy.Create<IAuraDbContext, OwnershipContextProxy>();
        var proxy = (OwnershipContextProxy)(object)db;
        proxy.Users = new CheckDbSet<User>([user]);
        proxy.Conversations = new CheckDbSet<Conversation>([conversation]);
        proxy.Projects = new CheckDbSet<Project>([project]);
        var input = new LiveOwnershipInput(secret, subject, internalId,
            conversationId, projectId);
        check(await LiveOwnershipVerification.VerifySourcesAsync(db, input, default) ==
            LiveOwnershipStage.Passed && proxy.Saves == 0,
            "12F offline seam exercises reviewed mapping, ownership negatives, composition read-only");
        check(await LiveOwnershipVerification.VerifySourcesAsync(db,
                input with { InternalUserId = Guid.NewGuid() }, default) ==
            LiveOwnershipStage.MappingFailed,
            "12F expected internal ID must match exact mapping");
        project.UserId = Guid.NewGuid();
        check(await LiveOwnershipVerification.VerifySourcesAsync(db, input, default) ==
            LiveOwnershipStage.OwnershipFailed,
            "12F foreign project ownership fails positive live stage");
        project.UserId = internalId;
        user.IsActive = false;
        check(await LiveOwnershipVerification.VerifySourcesAsync(db, input, default) ==
            LiveOwnershipStage.MappingFailed && proxy.Saves == 0,
            "12F inactive external subject fails mapping stage without write");
    }
}
