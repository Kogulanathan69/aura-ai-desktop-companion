using System.Reflection;
using System.Text.Json;
using Aura.Api.Endpoints;
using Aura.Application.Common.Exceptions;
using Aura.Application.Common.Interfaces;
using Aura.Application.ProjectMemories.DTOs;
using Aura.Application.ProjectMemories.Interfaces;
using Aura.Application.ProjectMemories.Services;
using Aura.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

internal static class MemoryChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var user = Guid.NewGuid();
        var owned = new Project { Id = Guid.NewGuid(), UserId = user };
        var otherOwned = new Project { Id = Guid.NewGuid(), UserId = user };
        var foreign = new Project { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var goodSession = new ProjectSession { Id = Guid.NewGuid(), ProjectId = owned.Id, UserId = user };
        var otherSession = new ProjectSession { Id = Guid.NewGuid(), ProjectId = otherOwned.Id, UserId = user };
        var foreignSession = new ProjectSession { Id = Guid.NewGuid(), ProjectId = owned.Id, UserId = foreign.UserId };
        var otherMemory = new ProjectMemory { Id = Guid.NewGuid(), ProjectId = otherOwned.Id, Title = "Other" };
        var rows = new List<ProjectMemory> { otherMemory };
        var context = DispatchProxy.Create<IAuraDbContext, UpdateContextProxy>();
        var proxy = (UpdateContextProxy)(object)context;
        proxy.Projects = new CheckDbSet<Project>(new List<Project> { owned, otherOwned, foreign });
        proxy.Memories = new CheckDbSet<ProjectMemory>(rows);
        proxy.Sessions = new CheckDbSet<ProjectSession>(new List<ProjectSession> { goodSession, otherSession, foreignSession });
        var clock = new CheckClock();
        var service = new ProjectMemoryService(context, new CheckIdentity(user), clock);
        var request = new CreateProjectMemoryRequest(" context ", " Title ", " Content ");
        var created = await service.CreateAsync(owned.Id, request);
        var memory = rows.Single(x => x.ProjectId == owned.Id);
        check(created is not null && created.Id != Guid.Empty && created.ProjectId == owned.Id &&
            created.Type == "Context" && created.Title == "Title" && created.Content == "Content" && created.Importance == 3,
            "owned project creates normalized manual memory with server identity and default importance");
        check(memory.SourceType == "Manual" && memory.Embedding.Length == 768 && memory.Embedding.All(x => x == 0) && memory.SessionId is null &&
            memory.CreatedAt == clock.UtcNow && memory.UpdatedAt == clock.UtcNow && memory.AccessCount == 0 && memory.LastAccessedAt is null,
            "memory initializes Manual source, no-embedding zero sentinel and conservative access metadata");
        var before = proxy.Saves;
        check(await service.CreateAsync(foreign.Id, request) is null && proxy.Saves == before,
            "foreign project cannot create memory");
        check(await service.GetByProjectAsync(foreign.Id) is null && await service.GetByIdAsync(foreign.Id, memory.Id) is null,
            "foreign project cannot list or read memory");
        check(await service.GetByIdAsync(owned.Id, otherMemory.Id) is null,
            "cross-project memory id cannot be read");
        var update = new UpdateProjectMemoryRequest(" decision ", " Changed ", " New content ", 5, goodSession.Id);
        check(await service.UpdateAsync(owned.Id, otherMemory.Id, update) is null &&
            !await service.DeleteAsync(owned.Id, otherMemory.Id) && otherMemory.Title == "Other",
            "cross-project memory id cannot be updated or deleted");
        check(await service.UpdateAsync(foreign.Id, memory.Id, update) is null && !await service.DeleteAsync(foreign.Id, memory.Id),
            "foreign project cannot update or delete memory");
        var linked = await service.CreateAsync(owned.Id, request with { SessionId = goodSession.Id });
        check(linked?.SessionId == goodSession.Id, "same-project local-user session accepted");

        async Task Reject(CreateProjectMemoryRequest invalid, string label)
        {
            var saves = proxy.Saves;
            var count = rows.Count;
            var rejected = false;
            try { await service.CreateAsync(owned.Id, invalid); }
            catch (AppValidationException) { rejected = true; }
            check(rejected && proxy.Saves == saves && rows.Count == count, label);
        }
        await Reject(request with { Type = "AI" }, "unsupported type rejected");
        await Reject(request with { Type = " " }, "blank type rejected");
        await Reject(request with { Title = " " }, "whitespace title rejected");
        await Reject(request with { Title = new string('x', 201) }, "oversized title rejected");
        await Reject(request with { Content = "\t " }, "whitespace content rejected");
        await Reject(request with { Importance = 0 }, "importance below range rejected");
        await Reject(request with { Importance = 6 }, "importance above range rejected");
        await Reject(request with { SessionId = otherSession.Id }, "other-project session rejected");
        await Reject(request with { SessionId = foreignSession.Id }, "other-user session rejected even with matching project id");
        await Reject(request with { SessionId = Guid.NewGuid() }, "missing session rejected");

        var embedding = memory.Embedding;
        var createdAt = memory.CreatedAt;
        memory.AccessCount = 7;
        memory.LastAccessedAt = clock.UtcNow;
        var changed = await service.UpdateAsync(owned.Id, memory.Id, update);
        check(changed?.Type == "Decision" && changed.Importance == 5 && changed.SessionId == goodSession.Id &&
            changed.Title == "Changed" && changed.Content == "New content" && memory.ProjectId == owned.Id &&
            memory.SourceType == "Manual" && ReferenceEquals(memory.Embedding, embedding) && memory.CreatedAt == createdAt &&
            memory.AccessCount == 7 && memory.LastAccessedAt == clock.UtcNow,
            "update changes only mutable fields, preserving project/source/embedding/creation/access identity");
        var savesBeforeInvalidUpdate = proxy.Saves;
        var denied = false;
        try { await service.UpdateAsync(owned.Id, memory.Id, update with { Title = "Must not change", SessionId = foreignSession.Id }); }
        catch (AppValidationException) { denied = true; }
        check(denied && memory.Title == "Changed" && proxy.Saves == savesBeforeInvalidUpdate,
            "invalid update session rejected before mutation/save");
        check((await service.UpdateAsync(owned.Id, memory.Id, update with { SessionId = null }))?.SessionId is null,
            "update explicitly clears optional session");
        var beforeRead = proxy.Saves;
        var list = await service.GetByProjectAsync(owned.Id);
        check(list is not null && list.Count == 2 && list.All(x => x.ProjectId == owned.Id) && list[0].Id == memory.Id,
            "list scopes memories and orders importance descending");
        var newest = new ProjectMemory { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), ProjectId = owned.Id,
            Importance = 5, UpdatedAt = clock.UtcNow.AddDays(1) };
        var tie = new ProjectMemory { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), ProjectId = owned.Id,
            Importance = 5, UpdatedAt = newest.UpdatedAt };
        rows.Add(tie);
        rows.Add(newest);
        var sorted = await service.GetByProjectAsync(owned.Id);
        check(sorted![0].Id == newest.Id && sorted[1].Id == tie.Id && sorted[2].Id == memory.Id,
            "equal-importance list orders UpdatedAt and then Id descending deterministically");
        _ = await service.GetByIdAsync(owned.Id, memory.Id);
        check(proxy.Saves == beforeRead && memory.AccessCount == 7 && memory.LastAccessedAt == clock.UtcNow,
            "list/detail reads do not mutate access metadata or save");
        foreach (var invalid in new[] { update with { Type = "Unknown" }, update with { Title = " " },
            update with { Content = " " }, update with { Importance = 6 } })
        {
            var beforeInvalid = proxy.Saves;
            var previousContent = memory.Content;
            var rejected = false;
            try { await service.UpdateAsync(owned.Id, memory.Id, invalid); }
            catch (AppValidationException) { rejected = true; }
            check(rejected && memory.Content == previousContent && proxy.Saves == beforeInvalid,
                "invalid memory update rejected without mutation/save");
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellationObserved = false;
        try { await service.GetByProjectAsync(owned.Id, cancelled.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        check(cancellationObserved, "memory queries propagate cancellation");
        foreach (var type in new[] { "Context", "Progress", "Decision", "Problem", "Architecture", "Note" })
            check((await service.CreateAsync(owned.Id, request with { Type = type.ToLowerInvariant(), Importance = 1 }))?.Type == type,
                "canonical memory type " + type);
        check(await service.DeleteAsync(owned.Id, memory.Id) && !rows.Contains(memory) &&
            !await service.DeleteAsync(owned.Id, memory.Id), "owned memory deletion succeeds; missing deletion returns false");
        foreach (var dto in new[] { typeof(CreateProjectMemoryRequest), typeof(UpdateProjectMemoryRequest) })
            check(!dto.GetProperties().Any(x => new[] { "Id", "ProjectId", "UserId", "AuthUserId", "SourceType", "Embedding" }.Contains(x.Name)),
                dto.Name + " excludes server-controlled fields");
        var injected = JsonSerializer.Deserialize<CreateProjectMemoryRequest>(
            "{\"Type\":\"Note\",\"Title\":\"t\",\"Content\":\"c\",\"ProjectId\":\"" + foreign.Id +
            "\",\"UserId\":\"" + foreign.UserId + "\",\"SourceType\":\"AI\",\"Embedding\":[1]}")!;
        var injectedResult = await service.CreateAsync(owned.Id, injected);
        check(injectedResult?.ProjectId == owned.Id && injectedResult.SourceType == "Manual" &&
            rows.Single(x => x.Id == injectedResult.Id).Embedding is { Length: 768 } sentinel && sentinel.All(x => x == 0),
            "extra JSON identity/source/embedding fields cannot influence create");
        check(!typeof(ProjectMemoryDto).GetProperties().Any(x => new[] { "Embedding", "UserId", "AuthUserId" }.Contains(x.Name)),
            "response DTO excludes embedding and user identifiers");

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<IProjectMemoryService>(_ => service);
        await using var app = builder.Build();
        app.MapProjectMemoryEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).ToArray();
        check(endpoints.Length == 5 && endpoints.All(x => x.Metadata.GetMetadata<IAuthorizeData>() is not null),
            "all five memory endpoints require authorization");
    }
}
