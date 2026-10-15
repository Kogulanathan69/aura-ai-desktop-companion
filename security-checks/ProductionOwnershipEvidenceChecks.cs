using Aura.Api.Security;
using Aura.Application.Common.Interfaces;
using Aura.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

internal static class ProductionOwnershipEvidenceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var output = new List<string>();
        var reads = new List<string>();
        var calls = 0;
        Task<LiveOwnershipStage> Pass(LiveOwnershipInput _, CancellationToken __)
        {
            calls++;
            return Task.FromResult(LiveOwnershipStage.Passed);
        }
        string? ReadOnlyGate(string key)
        {
            reads.Add(key);
            return key == LiveOwnershipVerification.ProductionEnableVariable ? null :
                throw new InvalidOperationException("Input read before opt-in.");
        }
        check(await LiveOwnershipVerification.RunProductionAsync(ReadOnlyGate, output.Add, Pass) == 0 &&
            output.SequenceEqual(["PRODUCTION OWNERSHIP EVIDENCE: SKIPPED"]) &&
            reads.SequenceEqual([LiveOwnershipVerification.ProductionEnableVariable]) && calls == 0,
            "15A missing opt-in skips before connection and DB access");

        output.Clear();
        reads.Clear();
        string? NoDisposable(string key)
        {
            reads.Add(key);
            return key == LiveOwnershipVerification.ProductionEnableVariable ? "true" :
                key == LiveOwnershipVerification.DisposableVariable ? null :
                throw new InvalidOperationException("Input read before disposable acknowledgement.");
        }
        check(await LiveOwnershipVerification.RunProductionAsync(NoDisposable, output.Add, Pass) == 0 &&
            output.SequenceEqual(["PRODUCTION OWNERSHIP EVIDENCE: SKIPPED"]) &&
            reads.SequenceEqual([LiveOwnershipVerification.ProductionEnableVariable,
                LiveOwnershipVerification.DisposableVariable]) && calls == 0,
            "15A missing disposable acknowledgement skips before connection and DB access");

        var subject = Guid.NewGuid();
        var internalUser = Guid.NewGuid();
        var project = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        const string secret = "Host=localhost;Database=aura_test;Password=secret-value";
        var values = new Dictionary<string, string?>
        {
            [LiveOwnershipVerification.ProductionEnableVariable] = "true",
            [LiveOwnershipVerification.DisposableVariable] = "true",
            ["AURA_VERIFY_AUTH_USER_ID"] = subject.ToString("D"),
            ["AURA_VERIFY_INTERNAL_USER_ID"] = internalUser.ToString("D"),
            ["AURA_VERIFY_PROJECT_ID"] = project.ToString("D"),
            ["AURA_VERIFY_CONVERSATION_ID"] = conversation.ToString("D"),
            ["ConnectionStrings__DefaultConnection"] = secret
        };
        string? Read(string key) { reads.Add(key); return values.GetValueOrDefault(key); }
        foreach (var key in new[] { "AURA_VERIFY_AUTH_USER_ID", "AURA_VERIFY_INTERNAL_USER_ID",
            "AURA_VERIFY_PROJECT_ID", "AURA_VERIFY_CONVERSATION_ID" })
        {
            var saved = values[key];
            values[key] = "malformed";
            reads.Clear();
            output.Clear();
            check(await LiveOwnershipVerification.RunProductionAsync(Read, output.Add, Pass) == 1 &&
                output.SequenceEqual(["INPUT: FAIL"]) && calls == 0 &&
                !reads.Contains("ConnectionStrings__DefaultConnection"),
                "15A malformed GUID fails before connection read and DB runner");
            values[key] = saved;
        }
        values["ConnectionStrings__DefaultConnection"] = "not=a-valid-keyword";
        output.Clear();
        check(await LiveOwnershipVerification.RunProductionAsync(Read, output.Add, Pass) == 1 &&
            output.SequenceEqual(["INPUT: FAIL"]) && calls == 0,
            "15A malformed connection fails safely before DB runner");
        values["ConnectionStrings__DefaultConnection"] = secret;

        output.Clear();
        check(await LiveOwnershipVerification.RunProductionAsync(Read, output.Add, Pass) == 0 &&
            calls == 1 && output.Count == 8 && output.Last() == "RESULT: PASS" &&
            output.All(line => !line.Contains("secret-value", StringComparison.Ordinal) &&
                !line.Contains(subject.ToString("D"), StringComparison.OrdinalIgnoreCase)),
            "15A explicit command emits bounded PASS categories without secrets or claims");
        foreach (var stage in Enum.GetValues<LiveOwnershipStage>()
            .Where(value => value != LiveOwnershipStage.Passed))
        {
            output.Clear();
            check(await LiveOwnershipVerification.RunProductionAsync(Read, output.Add,
                    (_, _) => Task.FromResult(stage)) == 1 &&
                output.Last() == "RESULT: FAIL" &&
                output.All(line => !line.Contains("secret-value", StringComparison.Ordinal)),
                "15A live failure emits bounded category and cannot pass");
        }
        output.Clear();
        check(await LiveOwnershipVerification.RunProductionAsync(Read, output.Add,
                (_, _) => throw new InvalidOperationException(secret)) == 1 &&
            output.SequenceEqual(["RESULT: FAIL"]),
            "15A raw exception and connection string are suppressed");

        var db = DispatchProxy.Create<IAuraDbContext, OwnershipContextProxy>();
        var proxy = (OwnershipContextProxy)(object)db;
        var active = new User { Id = internalUser, AuthUserId = subject, IsActive = true };
        var ownedProject = new Project { Id = project, UserId = internalUser };
        var ownedConversation = new Conversation { Id = conversation,
            UserId = internalUser, ProjectId = project, Type = "Project" };
        proxy.Users = new CheckDbSet<User>([active]);
        proxy.Projects = new CheckDbSet<Project>([ownedProject]);
        proxy.Conversations = new CheckDbSet<Conversation>([ownedConversation]);
        var input = new LiveOwnershipInput(secret, subject, internalUser, conversation, project);
        check(await LiveOwnershipVerification.VerifyProductionSourcesAsync(db, input, default) ==
            LiveOwnershipStage.Passed && proxy.Saves == 0,
            "15A exact active subject, project, conversation and scope pass without writes");
        var inactiveSubject = Guid.NewGuid();
        var inactive = new User { Id = Guid.NewGuid(), AuthUserId = inactiveSubject, IsActive = false };
        proxy.Users = new CheckDbSet<User>([active, inactive]);
        check(await LiveOwnershipVerification.VerifyProductionSourcesAsync(db,
                input with { InactiveAuthUserId = inactiveSubject }, default) ==
            LiveOwnershipStage.Passed && proxy.Saves == 0,
            "15A existing inactive fixture cannot map while active scope still passes");
        inactive.IsActive = true;
        check(await LiveOwnershipVerification.VerifyProductionSourcesAsync(db,
                input with { InactiveAuthUserId = inactiveSubject }, default) ==
            LiveOwnershipStage.AuthNegativeFailed,
            "15A purported inactive fixture fails evidence if it is active");
        inactive.IsActive = false;
        active.IsActive = false;
        check(await LiveOwnershipVerification.VerifyProductionSourcesAsync(db, input, default) ==
            LiveOwnershipStage.MappingFailed && proxy.Saves == 0,
            "15A inactive user cannot map or authorize scope");
        active.IsActive = true;
        ownedConversation.ProjectId = Guid.NewGuid();
        check(await LiveOwnershipVerification.VerifyProductionSourcesAsync(db, input, default) ==
            LiveOwnershipStage.ConversationFailed,
            "15A cross-project conversation cannot authorize the target project");
        ownedConversation.ProjectId = project;
        ownedProject.UserId = Guid.NewGuid();
        check(await LiveOwnershipVerification.VerifyProductionSourcesAsync(db, input, default) ==
            LiveOwnershipStage.ProjectFailed,
            "15A foreign project cannot authorize the target user");

        var root = Directory.GetCurrentDirectory();
        string Source(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
        var program = Source("security-checks", "Program.cs");
        var harness = Source("security-checks", "LiveOwnershipVerification.cs");
        var api = Source("Aura.Api", "Program.cs");
        var inspector = ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection());
        check(program.Contains("args[0] == LiveOwnershipVerification.ProductionCommand", StringComparison.Ordinal) &&
            program.IndexOf("await ProductionOwnershipEvidenceChecks.RunAsync(Check)", StringComparison.Ordinal) >
            program.IndexOf("var checks = 0", StringComparison.Ordinal) &&
            !api.Contains("LiveOwnershipVerification", StringComparison.Ordinal),
            "15A command is explicit; normal offline suite and API startup do not run it");
        check(!harness.Contains("MigrateAsync(", StringComparison.Ordinal) &&
            !harness.Contains("EnsureCreated", StringComparison.Ordinal) &&
            !harness.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase) &&
            !harness.Contains("SaveChangesAsync(", StringComparison.Ordinal) &&
            !harness.Contains("AddHostedService", StringComparison.Ordinal) &&
            !harness.Contains("Task.Delay(", StringComparison.Ordinal),
            "15A harness contains no migration, write, broad cleanup, worker or retry loop");
        check(!inspector.Ownership && !inspector.CompositionComplete &&
            api.Contains("new UnavailableRuntimeCapabilityReadiness()", StringComparison.Ordinal) &&
            !api.Contains(".AddOwnershipRuntimeFoundation(", StringComparison.Ordinal) &&
            !api.Contains("AddScoped<ITrustedToolExecutionService", StringComparison.Ordinal),
            "15A evidence cannot activate ownership composition or production execution");
    }
}
