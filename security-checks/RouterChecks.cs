using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.DTOs;
using Aura.Application.AI.Interfaces;
using Aura.Application.AI.Models;
using Aura.Application.AI.Providers;
using Microsoft.Extensions.Configuration;

internal static class RouterChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var defaults = new ConfigurationBuilder().Build().GetSection(AiProviderRouterOptions.SectionName)
            .Get<AiProviderRouterOptions>() ?? new();
        check(defaults.Mode == AiProviderMode.Local, "router missing configuration preserves Local default");
        foreach (var mode in new[] { AiProviderMode.Local, AiProviderMode.Cloud })
        {
            var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["AI:Router:Mode"] = mode.ToString() }).Build().GetSection(AiProviderRouterOptions.SectionName).Get<AiProviderRouterOptions>()!;
            check(configured.Mode == mode, "router server configuration binds " + mode);
            foreach (var status in Enum.GetValues<AiGenerationStatus>())
            {
                var expected = status == AiGenerationStatus.Success ? new AiGenerationResult(status, "untrusted text", "Selected", "model") : new(status);
                var local = new RouterProviderFake { Result = expected };
                var cloud = new RouterProviderFake { Result = expected };
                var selected = mode == AiProviderMode.Local ? local : cloud;
                var other = mode == AiProviderMode.Local ? cloud : local;
                var router = new AiProviderRouter(local, cloud, configured);
                var request = new AiGenerationRequest("current explicit prompt");
                using var tokenSource = new CancellationTokenSource();
                var actual = await router.GenerateAsync(request, tokenSource.Token);
                check(ReferenceEquals(actual, expected) && selected.Calls == 1 && other.Calls == 0 &&
                    ReferenceEquals(selected.Request, request) && selected.Token == tokenSource.Token,
                    "router " + mode + " " + status + " passes result/input/token unchanged, exactly one selected call, no retry/fallback");
            }
            var cancelledLocal = new RouterProviderFake();
            var cancelledCloud = new RouterProviderFake();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var observed = false;
            try { await new AiProviderRouter(cancelledLocal, cancelledCloud, configured).GenerateAsync(new("prompt"), cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && cancelledLocal.Calls == 0 && cancelledCloud.Calls == 0, "router " + mode + " precancellation calls neither provider");
            using var during = new CancellationTokenSource();
            var selectedProvider = mode == AiProviderMode.Local ? cancelledLocal : cancelledCloud;
            selectedProvider.Generate = (_, token) => { during.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(selectedProvider.Result); };
            observed = false;
            try { await new AiProviderRouter(cancelledLocal, cancelledCloud, configured).GenerateAsync(new("prompt"), during.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && selectedProvider.Calls == 1 && cancelledLocal.Calls + cancelledCloud.Calls == 1,
                "router " + mode + " selected cancellation propagates without retry/fallback");
        }
        foreach (var mode in new[] { (AiProviderMode)(-1), (AiProviderMode)2, (AiProviderMode)99 })
        {
            var local = new RouterProviderFake();
            var cloud = new RouterProviderFake();
            check((await new AiProviderRouter(local, cloud, new() { Mode = mode }).GenerateAsync(new("prompt"))).Status ==
                AiGenerationStatus.InvalidConfiguration && local.Calls == 0 && cloud.Calls == 0, "invalid router mode calls neither provider");
        }
        check(typeof(AiProviderRouterOptions).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Mode" }),
            "router options contain only bounded selection, no secret/model/URL");
        check(typeof(AiGenerationRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Prompt" }) &&
            typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Prompt" }), "caller requests cannot select provider or supply extra context");
        var dependencies = typeof(AiChatService).GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();
        check(dependencies.Contains(typeof(IAiProviderRouter)) && !dependencies.Contains(typeof(ILocalAiProvider)) &&
            !dependencies.Contains(typeof(ICloudAiProvider)), "chat depends only on router for generation");
        var fixture = new ChatFixture();
        var unusedLocal = new RouterProviderFake();
        var cloudProvider = new RouterProviderFake { Result = new(AiGenerationStatus.Success, "cloud answer", "OpenAI", "configured") };
        var chat = new AiChatService(fixture.Context, new CheckIdentity(fixture.User), fixture.Clock,
            new AiProviderRouter(unusedLocal, cloudProvider, new() { Mode = AiProviderMode.Cloud }));
        var result = await chat.GenerateAsync(fixture.Conversation.Id, new(" Explicit prompt "));
        check(result.Status == AiChatStatus.Success && fixture.Proxy.Saves == 2 && fixture.Persisted.Count == 2 &&
            cloudProvider.Calls == 1 && unusedLocal.Calls == 0 && cloudProvider.Request?.Prompt == "Explicit prompt" &&
            result.AssistantMessage?.Content == "cloud answer", "chat cloud routing preserves two-phase persistence and only current normalized prompt");
    }
}

internal sealed class RouterProviderFake : ILocalAiProvider, ICloudAiProvider
{
    internal int Calls { get; private set; }
    internal AiGenerationRequest? Request { get; private set; }
    internal CancellationToken Token { get; private set; }
    internal AiGenerationResult Result { get; set; } = new(AiGenerationStatus.Disabled);
    internal Func<AiGenerationRequest, CancellationToken, Task<AiGenerationResult>>? Generate { get; set; }
    public Task<AiGenerationResult> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
    { Calls++; Request = request; Token = cancellationToken; return Generate?.Invoke(request, cancellationToken) ?? Task.FromResult(Result); }
}
