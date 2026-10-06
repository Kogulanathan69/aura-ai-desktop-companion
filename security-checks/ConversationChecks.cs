using System.Reflection;
using System.Text.Json;
using Aura.Api.Endpoints;
using Aura.Application.Common.Exceptions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Conversations.DTOs;
using Aura.Application.Conversations.Interfaces;
using Aura.Application.Conversations.Services;
using Aura.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

internal static class ConversationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var user = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), UserId = user };
        var foreignProject = new Project { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var foreign = new Conversation { Id = Guid.NewGuid(), UserId = foreignProject.UserId, Title = "Foreign" };
        var rows = new List<Conversation> { foreign };
        var messages = new List<Message>();
        var context = DispatchProxy.Create<IAuraDbContext, UpdateContextProxy>();
        var proxy = (UpdateContextProxy)(object)context;
        proxy.Projects = new CheckDbSet<Project>(new List<Project> { project, foreignProject });
        proxy.Conversations = new CheckDbSet<Conversation>(rows);
        proxy.Messages = new CheckDbSet<Message>(messages);
        var clock = new ConversationClock();
        var service = new ConversationService(context, new CheckIdentity(user), clock);
        var general = await service.CreateAsync(new(" General title "));
        var owned = rows.Single(x => x.Id == general!.Id);
        check(general is not null && general.Type == "General" && general.ProjectId is null && general.Title == "General title" &&
            owned.UserId == user && general.Summary is null && !general.IsArchived && general.LastMessageAt is null &&
            general.CreatedAt == clock.UtcNow && general.UpdatedAt == clock.UtcNow,
            "General conversation uses authenticated identity, default scope and server timestamps");
        var linked = await service.CreateAsync(new("Project title", " project ", project.Id));
        check(linked?.Type == "Project" && linked.ProjectId == project.Id, "owned Project conversation created canonically");
        var before = proxy.Saves;
        check(await service.CreateAsync(new("Denied", "Project", foreignProject.Id)) is null && proxy.Saves == before,
            "foreign project conversation rejected without write");
        check(await service.CreateAsync(new("Missing", "Project", Guid.NewGuid())) is null,
            "missing project and foreign project share not-found behavior");
        async Task RejectConversation(CreateConversationRequest request, string label)
        {
            var saves = proxy.Saves;
            var count = rows.Count;
            var rejected = false;
            try { await service.CreateAsync(request); } catch (AppValidationException) { rejected = true; }
            check(rejected && proxy.Saves == saves && rows.Count == count, label);
        }
        await RejectConversation(new("Title", "General", project.Id), "General rejects non-null ProjectId");
        await RejectConversation(new("Title", "Project"), "Project rejects missing ProjectId");
        await RejectConversation(new("Title", "Other"), "unsupported conversation type rejected");
        await RejectConversation(new(" "), "whitespace conversation title rejected");
        await RejectConversation(new(new string('x', 201)), "oversized conversation title rejected");
        check((await service.GetAllAsync()).All(x => x.Id != foreign.Id) && (await service.GetAllAsync()).Count == 2,
            "conversation list includes only owned valid scopes");
        check(await service.GetByIdAsync(foreign.Id) is null && await service.GetByIdAsync(Guid.NewGuid()) is null,
            "foreign and missing conversation detail indistinguishable");
        before = proxy.Saves;
        check(await service.UpdateAsync(foreign.Id, new("Changed", true)) is null && !foreign.IsArchived &&
            foreign.Title == "Foreign" && proxy.Saves == before, "foreign conversation cannot update or archive");

        owned.Summary = "Server summary";
        var originalCreatedAt = owned.CreatedAt;
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var updateJson = "{\"Title\":\" Updated \",\"IsArchived\":true,\"Type\":\"Project\",\"ProjectId\":\"" + project.Id +
            "\",\"UserId\":\"" + foreignProject.UserId + "\",\"Summary\":\"Forged\",\"CreatedAt\":\"2000-01-01T00:00:00Z\"}";
        await service.UpdateAsync(owned.Id, JsonSerializer.Deserialize<UpdateConversationRequest>(updateJson)!);
        check(owned.Type == "General" && owned.ProjectId is null && owned.UserId == user && owned.Summary == "Server summary" &&
            owned.CreatedAt == originalCreatedAt && owned.IsArchived && owned.Title == "Updated" && owned.UpdatedAt == clock.UtcNow,
            "update overposting cannot retarget scope/user/summary/creation metadata; archive succeeds");
        var projectRow = rows.Single(x => x.Id == linked!.Id);
        await service.UpdateAsync(projectRow.Id, JsonSerializer.Deserialize<UpdateConversationRequest>(
            "{\"Title\":\"Project updated\",\"IsArchived\":false,\"Type\":\"General\",\"ProjectId\":\"" + foreignProject.Id + "\"}")!);
        check(projectRow.Type == "Project" && projectRow.ProjectId == project.Id,
            "Project conversation update cannot become General or move to another project");
        await service.UpdateAsync(owned.Id, new("Updated", false));
        check(!owned.IsArchived, "owned conversation can unarchive");
        before = proxy.Saves;
        var badUpdate = false;
        try { await service.UpdateAsync(owned.Id, new(" ", true)); } catch (AppValidationException) { badUpdate = true; }
        check(badUpdate && !owned.IsArchived && proxy.Saves == before, "invalid update rejects before mutation/save");

        var createJson = "{\"Title\":\"Injection\",\"UserId\":\"" + foreignProject.UserId + "\",\"Summary\":\"Forged\",\"IsArchived\":true}";
        var injected = await service.CreateAsync(JsonSerializer.Deserialize<CreateConversationRequest>(createJson)!);
        check(rows.Single(x => x.Id == injected!.Id).UserId == user && injected!.Summary is null && !injected.IsArchived,
            "create overposting cannot control user/summary/archive");
        before = proxy.Saves;
        check(await service.CreateMessageAsync(foreign.Id, new("Denied")) is null && messages.Count == 0 && proxy.Saves == before,
            "foreign conversation rejects message creation");
        check(await service.GetMessagesAsync(foreign.Id) is null && await service.GetMessageAsync(foreign.Id, Guid.NewGuid()) is null,
            "foreign conversation denies all message reads");
        var rejectedContent = false;
        try { await service.CreateMessageAsync(owned.Id, new(" \t ")); } catch (AppValidationException) { rejectedContent = true; }
        check(rejectedContent && messages.Count == 0 && owned.LastMessageAt is null && proxy.Saves == before,
            "whitespace message rejected before insertion/timestamp mutation");
        var modelJson = "{\"Content\":\" Message \",\"Role\":\"System\",\"MessageType\":\"ToolResult\",\"ModelProvider\":\"Fake\",\"ModelName\":\"Fake\",\"TokenCount\":99,\"ConversationId\":\"" + foreign.Id + "\"}";
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var message = await service.CreateMessageAsync(owned.Id, JsonSerializer.Deserialize<CreateMessageRequest>(modelJson)!);
        check(message is not null && message.ConversationId == owned.Id && message.Content == "Message" && message.Role == "User" &&
            message.MessageType == "Text" && message.ModelProvider is null && message.ModelName is null && message.TokenCount is null &&
            message.CreatedAt == clock.UtcNow, "message overposting cannot impersonate roles/models or foreign parent");
        check(proxy.Saves == before + 1 && messages.Single().Id == message!.Id && owned.LastMessageAt == clock.UtcNow &&
            owned.UpdatedAt == clock.UtcNow, "message and parent timestamps changed in one SaveChanges call");
        var another = await service.CreateMessageAsync(projectRow.Id, new("Other parent"));
        check(await service.GetMessageAsync(owned.Id, another!.Id) is null,
            "message ID from another conversation is not authorized by owned parent");
        check((await service.GetMessageAsync(owned.Id, message!.Id))?.Content == "Message",
            "owned exact-parent message detail succeeds");
        messages.Add(new Message { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), ConversationId = owned.Id,
            CreatedAt = originalCreatedAt, Content = "Second tie" });
        messages.Add(new Message { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), ConversationId = owned.Id,
            CreatedAt = originalCreatedAt, Content = "First tie" });
        before = proxy.Saves;
        var history = await service.GetMessagesAsync(owned.Id);
        check(history?.Count == 3 && history.All(x => x.ConversationId == owned.Id) &&
            history[0].Content == "First tie" && history[1].Content == "Second tie" && history[2].Id == message.Id,
            "message history scopes parent and sorts CreatedAt/Id ascending");
        var list = await service.GetAllAsync();
        check(list[0].LastMessageAt.HasValue && list[1].LastMessageAt.HasValue && list.Skip(2).All(x => x.LastMessageAt is null),
            "conversation list places null LastMessageAt after active histories");
        check(proxy.Saves == before, "history retrieval performs no save");

        project.UserId = foreignProject.UserId;
        check(await service.GetByIdAsync(projectRow.Id) is null && await service.GetMessagesAsync(projectRow.Id) is null &&
            await service.GetMessageAsync(projectRow.Id, another.Id) is null &&
            await service.CreateMessageAsync(projectRow.Id, new("Denied")) is null &&
            await service.UpdateAsync(projectRow.Id, new("Denied", true)) is null &&
            (await service.GetAllAsync()).All(x => x.Id != projectRow.Id),
            "project-linked operations recheck current project owner, not just conversation owner");
        project.UserId = user;
        projectRow.ProjectId = null;
        check(await service.GetByIdAsync(projectRow.Id) is null, "orphaned Project scope fails closed after SetNull");

        foreach (var dto in new[] { typeof(CreateConversationRequest), typeof(UpdateConversationRequest), typeof(CreateMessageRequest) })
            check(!dto.GetProperties().Any(x => new[] { "UserId", "Summary", "Role", "MessageType", "ModelProvider", "ModelName", "TokenCount", "CreatedAt", "Id" }.Contains(x.Name)),
                dto.Name + " excludes server-owned fields");
        check(!typeof(UpdateConversationRequest).GetProperties().Any(x => x.Name is "Type" or "ProjectId"),
            "update DTO cannot express scope movement");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<IConversationService>(_ => service);
        await using var app = builder.Build();
        app.MapConversationEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).ToArray();
        check(endpoints.Length == 7 && endpoints.All(x => x.Metadata.GetMetadata<IAuthorizeData>() is not null),
            "all seven conversation/message routes require authorization");
        check(endpoints.All(x => !x.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("DELETE")),
            "no public hard-delete or message mutation route");
    }
}
internal sealed class ConversationClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
