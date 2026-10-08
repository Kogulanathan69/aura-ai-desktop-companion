using System.Reflection;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Security;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

internal static class PersistenceFoundationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var userId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var user = new User { Id = userId, IsActive = true };
        var conversation = new Conversation { Id = conversationId, UserId = userId,
            ProjectId = projectId, Type = "Project" };
        var project = new Project { Id = projectId, UserId = userId };
        var context = DispatchProxy.Create<IAuraDbContext, OwnershipContextProxy>();
        var proxy = (OwnershipContextProxy)(object)context;
        proxy.Users = new CheckDbSet<User>([user]);
        proxy.Conversations = new CheckDbSet<Conversation>([conversation]);
        proxy.Projects = new CheckDbSet<Project>([project]);
        var source = new OwnershipPersistenceSource(context);
        var request = new OwnershipLookupRequest(userId, conversationId, projectId);
        var result = await source.GetAsync(request, default);
        check(result == new OwnershipLookupResult(OwnershipLookupStatus.Found, request, true, true, true)
            && proxy.Saves == 0, "12A exact active user/conversation/project ownership is read-only");

        foreach (var other in new[] { request with { UserId = Guid.NewGuid() },
            request with { ConversationId = Guid.NewGuid() },
            request with { ProjectId = Guid.NewGuid() },
            request with { UserId = Guid.Empty }, request with { ConversationId = Guid.Empty },
            request with { ProjectId = Guid.Empty } })
            check((await source.GetAsync(other, default)).Status == OwnershipLookupStatus.Denied,
                "12A foreign, missing, or empty exact identity denied");
        conversation.ProjectId = Guid.NewGuid();
        check((await source.GetAsync(request, default)).Status == OwnershipLookupStatus.Denied,
            "12A exact conversation-to-project relation required");
        conversation.ProjectId = projectId;
        project.UserId = Guid.NewGuid();
        check((await source.GetAsync(request, default)).Status == OwnershipLookupStatus.Denied,
            "12A foreign project denied despite matching conversation");
        project.UserId = userId;
        conversation.UserId = Guid.NewGuid();
        check((await source.GetAsync(request, default)).Status == OwnershipLookupStatus.Denied,
            "12A foreign conversation denied despite owned project");
        conversation.UserId = userId;
        user.IsActive = false;
        check((await source.GetAsync(request, default)).Status == OwnershipLookupStatus.Denied,
            "12A inactive user denied");
        user.IsActive = true;
        conversation.Type = "General";
        check((await source.GetAsync(request, default)).Status == OwnershipLookupStatus.Denied,
            "12A general conversation cannot claim a project scope");
        conversation.Type = "Project";

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var propagated = false;
        try { await source.GetAsync(request, cancelled.Token); }
        catch (OperationCanceledException) { propagated = true; }
        check(propagated, "12A ownership query propagates cancellation");
        proxy.FailRead = true;
        var before = proxy.ConversationReads;
        result = await source.GetAsync(request, default);
        check(result.Status == OwnershipLookupStatus.Unavailable && result.Binding is null &&
            proxy.ConversationReads == before + 1 && proxy.Saves == 0,
            "12A database error unavailable without leakage, retry, fallback, or write");
        check(new PersistenceCapabilityReadiness().GetSnapshot() ==
            new RuntimeCapabilitySnapshot(true, false, false, false, false, false, false, false, false) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Ownership,
            "12A informational persistence readiness reports ownership only; startup remains unavailable");
    }
}

public class OwnershipContextProxy : DispatchProxy
{
    internal DbSet<User> Users { get; set; } = null!;
    internal DbSet<Conversation> Conversations { get; set; } = null!;
    internal DbSet<Project> Projects { get; set; } = null!;
    internal bool FailRead { get; set; }
    internal int ConversationReads { get; private set; }
    internal int Saves { get; private set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Users" => Users,
        "get_Projects" => Projects,
        "get_Conversations" => ReadConversations(),
        "SaveChangesAsync" => Save(),
        _ => throw new InvalidOperationException("Unexpected context operation.")
    };
    private DbSet<Conversation> ReadConversations()
    {
        ConversationReads++;
        if (FailRead) throw new InvalidOperationException("Synthetic private DB error.");
        return Conversations;
    }
    private Task<int> Save() { Saves++; return Task.FromResult(1); }
}
