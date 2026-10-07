using Aura.Application.Tools;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.Providers;
using Aura.Application.ProjectFiles.Content;

internal static class ToolChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ToolIdentifier Id(string value)
        {
            if (!ToolIdentifier.TryCreate(value, out var id)) throw new InvalidOperationException("Invalid fixture ID.");
            return id!;
        }
        ToolDescriptor Descriptor(string id = "fixture", string name = "Fixture", string description = "Synthetic fixture") =>
            new(Id(id), name, description, ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead);
        void Reject(Action action, string label)
        {
            var rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            check(rejected, label);
        }
        check(Id(" FIXTURE ") == Id("fixture"), "11N identifier normalization deterministic");
        check(Id("a" + new string('x', 63)).Value.Length == 64, "11N exact identifier limit accepted");
        foreach (var invalid in new string?[] { null, "", " ", new string('x', 65), "1tool", "a/b", "a\\b",
            "https://example.test", "tool.name", "a:b", "a\0b", "a b", "étool", "a;cmd" })
            check(!ToolIdentifier.TryCreate(invalid, out var id) && id is null, "11N blank/malformed/oversize/path/URL/type identifiers rejected");
        var handler = new FixtureHandler();
        var alternate = new FixtureHandler();
        var descriptor = Descriptor();
        var registrations = new List<ToolRegistration> { new(descriptor, handler), new(Descriptor("alternate"), alternate) };
        var registry = new ToolRegistry(registrations);
        check(registry.TryResolve(Id("fixture"), out var found, out var resolved) && ReferenceEquals(found, descriptor) &&
            ReferenceEquals(resolved, handler), "11N resolves exactly one server-registered handler");
        check(!registry.TryResolve(Id("unknown"), out found, out resolved) && found is null && resolved is null,
            "11N unknown identifier fails closed");
        check(registry.Descriptors.Select(x => x.Id.Value).SequenceEqual(new[] { "alternate", "fixture" }),
            "11N descriptors sorted ordinally");
        registrations.Clear();
        check(registry.Descriptors.Count == 2, "11N registry snapshots caller collection");
        check(((ICollection<ToolDescriptor>)registry.Descriptors).IsReadOnly, "11N descriptor collection read-only");
        check(typeof(ToolDescriptor).GetProperties().All(x => x.SetMethod is null) &&
            typeof(ToolIdentifier).GetProperties().All(x => x.SetMethod is null), "11N descriptors and identifiers immutable");
        Reject(() => new ToolRegistry([new(Descriptor("fixture"), handler), new(Descriptor(" FIXTURE "), alternate)]),
            "11N normalized duplicate IDs rejected");
        Reject(() => new ToolRegistry([new(descriptor, null!)]), "11N missing handler rejected");
        Reject(() => new ToolRegistry([new(null!, handler)]), "11N missing descriptor rejected");
        Reject(() => new ToolRegistry([null!]), "11N null registration rejected");
        foreach (var pair in new[] { ("", "text"), (" ", "text"), (new string('n', 81), "text"),
            ("name", ""), ("name", new string('d', 257)), ("name", "text\nmore"), ("name", "\ud800") })
            Reject(() => Descriptor(name: pair.Item1, description: pair.Item2), "11N malformed/oversized descriptor rejected");
        check(Descriptor(name: new string('n', 80), description: new string('d', 256)).Description.Length == 256,
            "11N exact descriptor bounds accepted");
        Reject(() => new ToolDescriptor(Id("fixture"), "Name", "Description", (ToolCapability)99,
            ToolPermissionRequirement.OwnedProjectRead), "11N undefined capability rejected");
        Reject(() => new ToolDescriptor(Id("fixture"), "Name", "Description", ToolCapability.ProjectRead,
            (ToolPermissionRequirement)99), "11N undefined permission metadata rejected");
        var consumed = 0;
        IEnumerable<ToolRegistration> Oversized()
        {
            for (var i = 0; i < 100; i++) { consumed++; yield return new(Descriptor("fixture-" + i), handler); }
        }
        Reject(() => new ToolRegistry(Oversized()), "11N registry count bound enforced");
        check(consumed == 33, "11N registry stops enumerating on first excess entry");
        check(new ToolRegistry(Enumerable.Range(0, 32).Select(i => new ToolRegistration(Descriptor("fixture-" + i), handler)))
            .Descriptors.Count == 32, "11N exact registry capacity accepted");
        var dispatcher = new DisabledToolDispatcher(registry);
        check((await dispatcher.ExecuteAsync(null)).Status == ToolExecutionStatus.InvalidRequest &&
            (await dispatcher.ExecuteAsync(new(null!))).Status == ToolExecutionStatus.InvalidRequest,
            "11N malformed invocation safely rejected");
        check((await dispatcher.ExecuteAsync(new(Id("unknown")))).Status == ToolExecutionStatus.UnknownTool,
            "11N dispatch unknown tool fails closed without fallback");
        handler.ThrowFailure = true;
        var result = await dispatcher.ExecuteAsync(new(Id("fixture")));
        check(result.Status == ToolExecutionStatus.Disabled && handler.Calls == 0 && alternate.Calls == 0,
            "11N disabled/registered/throwing handler never invoked; no retry or fallback");
        check(!descriptor.Enabled && descriptor.RequiresApproval && descriptor.RequiresOwnedScope,
            "11N every descriptor disabled and requires approval/owned scope");
        check(result.Message == "Tool execution is disabled." && !result.Message.Contains("secret"),
            "11N potentially secret handler exceptions cannot reach result");
        check(typeof(ToolInvocationRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "ToolId" }),
            "11N request contains no arguments/paths/commands/URLs/permission/approval flags");
        check(typeof(ToolExecutionResult).GetConstructors().Length == 0 &&
            typeof(ToolExecutionResult).GetProperties().All(x => x.SetMethod is null),
            "11N result accepts no arbitrary secret output or exception payload");
        foreach (var status in Enum.GetValues<ToolExecutionStatus>())
            check(ToolExecutionResult.FromStatus(status).Message.Length <= ToolLimits.MaximumResultMessageCharacters,
                "11N fixed result message bound " + status);
        check(ToolExecutionResult.FromStatus((ToolExecutionStatus)999).Status == ToolExecutionStatus.Failed,
            "11N malformed status maps to fixed safe failure");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = false;
        try { await dispatcher.ExecuteAsync(new(Id("fixture")), cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && handler.Calls == 0, "11N dispatcher cancellation propagates before resolution/execution");
        check(typeof(IToolHandler).GetMethod("ExecuteAsync")!.GetParameters().Last().ParameterType == typeof(CancellationToken),
            "11N future handler contract accepts cancellation");
        check(typeof(ToolRegistry).GetConstructors().Single().GetParameters().Single().ParameterType == typeof(IEnumerable<ToolRegistration>),
            "11N explicit instance registration only, no type/assembly/plugin loading contract");
        check(typeof(DisabledToolDispatcher).GetConstructors().Single().GetParameters().Single().ParameterType == typeof(ToolRegistry),
            "11N denial boundary has no DB/persistence/permission grant/provider/OS dependencies");
        foreach (var type in new[] { typeof(AiChatService), typeof(AiProviderRouter) })
            check(type.GetConstructors().Single().GetParameters().All(x => x.ParameterType.Namespace != typeof(IToolHandler).Namespace),
                "11N " + type.Name + " remains tool-free");
        check(!new ProjectFileAccessOptions().Enabled, "11N Safe File Access remains disabled");
        check((await new DisabledToolDispatcher(new ToolRegistry([])).ExecuteAsync(new(Id("fixture")))).Status ==
            ToolExecutionStatus.UnknownTool, "11N empty registry enables nothing");
    }

    private sealed class FixtureHandler : IToolHandler
    {
        public int Calls;
        public bool ThrowFailure;
        public Task<ToolExecutionResult> ExecuteAsync(ToolInvocationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (ThrowFailure) throw new InvalidOperationException("secret raw exception fixture");
            return Task.FromResult(ToolExecutionResult.FromStatus(ToolExecutionStatus.Success));
        }
    }
}
