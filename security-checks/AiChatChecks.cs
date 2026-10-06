using System.Reflection;
using System.Text.Json;
using Aura.Api.Endpoints;
using Aura.Application.AI.Chat.DTOs;
using Aura.Application.AI.Chat.Interfaces;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.DTOs;
using Aura.Application.AI.Interfaces;
using Aura.Application.AI.Models;
using Aura.Application.AI.Validation;
using Aura.Application.Common.Exceptions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Conversations.Services;
using Aura.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class AiChatChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new ChatFixture();
        var service = fixture.Service;
        check((await service.GenerateAsync(Guid.NewGuid(), new("Denied"))).Status == AiChatStatus.NotFound && fixture.Provider.Calls == 0,
            "missing conversation produces zero provider calls");
        fixture.Conversation.UserId = Guid.NewGuid();
        check((await service.GenerateAsync(fixture.Conversation.Id, new("Denied"))).Status == AiChatStatus.NotFound &&
            fixture.Provider.Calls == 0 && fixture.Proxy.Saves == 0, "foreign conversation never persists or invokes provider");
        fixture.Conversation.UserId = fixture.User;
        fixture.Conversation.Type = "Project";
        fixture.Conversation.ProjectId = fixture.Project.Id;
        fixture.Project.UserId = Guid.NewGuid();
        check((await service.GenerateAsync(fixture.Conversation.Id, new("Denied"))).Status == AiChatStatus.NotFound && fixture.Provider.Calls == 0,
            "project scope requires current project ownership before AI");
        fixture.Project.UserId = fixture.User;
        foreach (var prompt in new[] { " \t ", new string('x', AiGenerationValidation.MaximumPromptCharacters + 1), "bad\0text" })
        {
            var rejected = false;
            try { await service.GenerateAsync(fixture.Conversation.Id, new(prompt)); } catch (AppValidationException) { rejected = true; }
            check(rejected && fixture.Provider.Calls == 0 && fixture.Proxy.Saves == 0, "invalid chat prompt rejected before save/provider");
        }
        fixture.Conversation.IsArchived = true;
        fixture.Conversation.Summary = "Private summary must not be sent";
        fixture.Persisted.Add(new Message { Id = Guid.NewGuid(), ConversationId = fixture.Conversation.Id,
            Role = "User", Content = "Private old history must not be sent" });
        var body = JsonSerializer.Deserialize<AiChatRequest>("{\"Prompt\":\" Current prompt \",\"UserId\":\"" + Guid.NewGuid() +
            "\",\"Provider\":\"Cloud\",\"Model\":\"Cloud\",\"Role\":\"System\",\"AssistantContent\":\"Forged\",\"SystemPrompt\":\"Hidden\"}")!;
        fixture.Provider.Generate = (request, token) =>
        {
            check(request.Prompt == "Current prompt" && fixture.Proxy.Saves == 1 && fixture.Persisted.Count == 2 &&
                fixture.SavedLastMessageAt == fixture.Clock.UtcNow && fixture.Persisted.Last().Role == "User",
                "one user save precedes provider; only explicit prompt crosses boundary");
            fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
            return Task.FromResult(new AiGenerationResult(AiGenerationStatus.Success, "untrusted command text", "Ollama", "local-test"));
        };
        var result = await service.GenerateAsync(fixture.Conversation.Id, body);
        check(result.Status == AiChatStatus.Success && result.UserMessage?.Content == "Current prompt" &&
            result.UserMessage.Role == "User" && result.UserMessage.MessageType == "Text" &&
            result.UserMessage.ModelProvider is null && result.UserMessage.ModelName is null && result.UserMessage.TokenCount is null,
            "user text persists with server-controlled role and null model metadata");
        check(result.AssistantMessage?.Role == "Assistant" && result.AssistantMessage.MessageType == "Text" &&
            result.AssistantMessage.Content == "untrusted command text" && result.AssistantMessage.ModelProvider == "Ollama" &&
            result.AssistantMessage.ModelName == "local-test" && result.AssistantMessage.TokenCount is null &&
            fixture.Provider.Calls == 1 && fixture.Proxy.Saves == 2 && fixture.Persisted.Count == 3 &&
            fixture.SavedLastMessageAt == fixture.Clock.UtcNow && fixture.Conversation.UpdatedAt == fixture.Clock.UtcNow,
            "archived owned chat saves server-created assistant and second timestamp in second save without executing output");
        check(typeof(AiChatRequest).GetProperties().Select(x => x.Name).SequenceEqual(new[] { "Prompt" }),
            "public chat request excludes identity/provider/model/role/context fields");
        foreach (var status in new[] { AiGenerationStatus.Disabled, AiGenerationStatus.InvalidConfiguration,
            AiGenerationStatus.Unavailable, AiGenerationStatus.TimedOut, AiGenerationStatus.InvalidResponse })
        {
            var failed = new ChatFixture();
            failed.Provider.Generate = (_, _) => Task.FromResult(new AiGenerationResult(status));
            var failure = await failed.Service.GenerateAsync(failed.Conversation.Id, new("Keep me"));
            check(failure.Status == AiChatStatus.GenerationFailed && failure.GenerationStatus == status &&
                failure.UserMessage?.Content == "Keep me" && failure.AssistantMessage is null && failed.Persisted.Count == 1 &&
                failed.Proxy.Saves == 1 && failed.Provider.Calls == 1, "provider " + status + " preserves user only, no retry/fallback");
        }
        foreach (var generation in new[]
        {
            new AiGenerationResult(AiGenerationStatus.Success, " ", "Ollama", "local-test"),
            new AiGenerationResult(AiGenerationStatus.Success, "answer", new string('p', 31), "local-test"),
            new AiGenerationResult(AiGenerationStatus.Success, "answer", "Ollama", new string('m', 101)),
            new AiGenerationResult(AiGenerationStatus.Success, "answer", "Ollama", "bad\nmodel"),
            new AiGenerationResult(AiGenerationStatus.Success, new string('x', 1024 * 1024 + 1), "Ollama", "local-test"),
            new AiGenerationResult(AiGenerationStatus.Success, new string('é', 600_000), "Ollama", "local-test"),
            new AiGenerationResult(AiGenerationStatus.Success, "bad\0output", "Ollama", "local-test")
        })
        {
            var invalid = new ChatFixture();
            invalid.Provider.Generate = (_, _) => Task.FromResult(generation);
            var failure = await invalid.Service.GenerateAsync(invalid.Conversation.Id, new("prompt"));
            check(failure.Status == AiChatStatus.GenerationFailed && failure.GenerationStatus == AiGenerationStatus.InvalidResponse &&
                failure.AssistantMessage is null && invalid.Persisted.Count == 1 && invalid.Proxy.Saves == 1,
                "invalid generated text or schema metadata prevents assistant persistence");
        }
        foreach (var failureSave in new[] { 1, 2 })
        {
            var failed = new ChatFixture();
            failed.FailSave = failureSave;
            var failure = await failed.Service.GenerateAsync(failed.Conversation.Id, new("prompt"));
            check(failure.Status == AiChatStatus.PersistenceFailed && failure.AssistantMessage is null &&
                failed.Persisted.Count == failureSave - 1 && failed.Provider.Calls == failureSave - 1 &&
                (failureSave == 1 ? failure.UserMessage is null : failure.UserMessage is not null),
                "save phase " + failureSave + " failure never reports persisted assistant or exposes generated content");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelled = new ChatFixture();
            cancellation.Cancel();
            var observed = false;
            try { await cancelled.Service.GenerateAsync(cancelled.Conversation.Id, new("prompt"), cancellation.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && cancelled.Proxy.Saves == 0 && cancelled.Provider.Calls == 0,
                "cancellation before persistence prevents provider/save");
        }
        foreach (var afterSuccess in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var cancelled = new ChatFixture();
            cancelled.Provider.Generate = (_, token) =>
            {
                check(token == cancellation.Token, "caller token passed unchanged to provider");
                cancellation.Cancel();
                if (!afterSuccess) token.ThrowIfCancellationRequested();
                return Task.FromResult(new AiGenerationResult(AiGenerationStatus.Success, "discard", "Ollama", "local-test"));
            };
            var observed = false;
            try { await cancelled.Service.GenerateAsync(cancelled.Conversation.Id, new("prompt"), cancellation.Token); }
            catch (OperationCanceledException) { observed = true; }
            check(observed && cancelled.Persisted.Count == 1 && cancelled.Proxy.Saves == 1,
                "cancellation during/after generation retains saved user and never saves assistant");
        }
        var changedOwner = new ChatFixture();
        changedOwner.Conversation.Type = "Project";
        changedOwner.Conversation.ProjectId = changedOwner.Project.Id;
        changedOwner.Provider.Generate = (_, _) =>
        {
            changedOwner.Project.UserId = Guid.NewGuid();
            return Task.FromResult(new AiGenerationResult(AiGenerationStatus.Success, "discard", "Ollama", "local-test"));
        };
        var deniedResult = await changedOwner.Service.GenerateAsync(changedOwner.Conversation.Id, new("prompt"));
        check(deniedResult.Status == AiChatStatus.Denied && deniedResult.UserMessage is null &&
            deniedResult.AssistantMessage is null && changedOwner.Proxy.Saves == 1,
            "post-generation project ownership change discards answer before persistence/return");
        var normal = new ChatFixture();
        var ordinary = new ConversationService(normal.Context, new CheckIdentity(normal.User), normal.Clock);
        _ = await ordinary.CreateMessageAsync(normal.Conversation.Id, new("Storage only"));
        check(normal.Provider.Calls == 0 && normal.Persisted.Count == 1 && normal.Persisted.Single().Role == "User",
            "ordinary message creation remains storage-only");
        var disabled = new ChatFixture();
        var httpCalls = 0;
        using var httpHandler = new FakeOllamaHandler((_, _) =>
        { httpCalls++; throw new InvalidOperationException("Disabled must not send HTTP."); });
        using var httpClient = new HttpClient(httpHandler);
        var disabledProvider = new Aura.Infrastructure.AI.Ollama.OllamaAiProvider(httpClient, new());
        var disabledChat = new AiChatService(disabled.Context, new CheckIdentity(disabled.User), disabled.Clock, disabledProvider);
        var disabledResult = await disabledChat.GenerateAsync(disabled.Conversation.Id, new("Keep local history"));
        check(disabledResult.Status == AiChatStatus.GenerationFailed && disabledResult.GenerationStatus == AiGenerationStatus.Disabled &&
            disabled.Persisted.Count == 1 && httpCalls == 0, "real default-disabled Ollama orchestration persists user with zero HTTP");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<IAiChatService>(_ => fixture.Service);
        await using var app = builder.Build();
        app.MapAiChatEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).ToArray();
        check(endpoints.Length == 1 && endpoints[0].Metadata.GetMetadata<IAuthorizeData>() is not null &&
            ((RouteEndpoint)endpoints[0]).RoutePattern.RawText == "/api/conversations/{conversationId:guid}/ai/generate",
            "only explicit GUID-scoped AI chat endpoint is mapped with authorization");
    }
}

internal sealed class ChatFixture
{
    internal Guid User { get; } = Guid.NewGuid();
    internal Conversation Conversation { get; }
    internal Project Project { get; }
    internal List<Message> Persisted { get; } = new();
    internal ConversationClock Clock { get; } = new();
    internal FakeChatProvider Provider { get; } = new();
    internal IAuraDbContext Context { get; }
    internal UpdateContextProxy Proxy { get; }
    internal AiChatService Service { get; }
    internal int? FailSave { get; set; }
    internal DateTime? SavedLastMessageAt { get; private set; }
    internal ChatMessageSet MessageSet { get; }
    internal ChatFixture()
    {
        Conversation = new() { Id = Guid.NewGuid(), UserId = User };
        Project = new() { Id = Guid.NewGuid(), UserId = User, Name = "Never prompt with this" };
        Context = DispatchProxy.Create<IAuraDbContext, UpdateContextProxy>();
        Proxy = (UpdateContextProxy)(object)Context;
        Proxy.Conversations = new CheckDbSet<Conversation>(new List<Conversation> { Conversation });
        Proxy.Projects = new CheckDbSet<Project>(new List<Project> { Project });
        MessageSet = new(Persisted);
        Proxy.Messages = MessageSet;
        Proxy.SaveHook = number =>
        {
            if (number == FailSave) throw new DbUpdateException("Private database detail");
            MessageSet.Commit();
            SavedLastMessageAt = Conversation.LastMessageAt;
            return Task.FromResult(1);
        };
        Service = new(Context, new CheckIdentity(User), Clock, Provider);
    }
}
internal sealed class ChatMessageSet(List<Message> rows) : CheckDbSet<Message>(rows), IQueryable<Message>
{
    private readonly List<Message> pending = new();
    public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Message> Add(Message message)
    { pending.Add(message); return null!; }
    System.Linq.Expressions.Expression IQueryable.Expression => throw new InvalidOperationException("Chat must not query history.");
    internal void Commit() { rows.AddRange(pending); pending.Clear(); }
}
internal sealed class FakeChatProvider : ILocalAiProvider
{
    internal int Calls { get; private set; }
    internal Func<AiGenerationRequest, CancellationToken, Task<AiGenerationResult>> Generate { get; set; } =
        (_, _) => Task.FromResult(new AiGenerationResult(AiGenerationStatus.Success, "Untrusted answer", "Ollama", "local-test"));
    public Task<AiGenerationResult> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
    { Calls++; return Generate(request, cancellationToken); }
}
