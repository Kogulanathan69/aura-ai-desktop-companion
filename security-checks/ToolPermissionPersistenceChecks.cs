using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

internal static class ToolPermissionPersistenceChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var userId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        ToolIdentifier.TryCreate("check-tool", out var toolId);
        var scope = new Aura.Application.Actions.ActionScope(userId, conversationId, projectId);
        var request = new ToolPermissionValidationRequest(scope, toolId!,
            ToolPermissionRequirement.OwnedProjectRead);
        var user = new User { Id = userId, IsActive = true };
        var conversation = new Conversation { Id = conversationId, UserId = userId,
            ProjectId = projectId, Type = "Project" };
        var project = new Project { Id = projectId, UserId = userId };
        var grant = new ToolPermissionGrantRecord { Id = Guid.NewGuid(), UserId = userId,
            ConversationId = conversationId, ProjectId = projectId, ToolId = toolId!.Value,
            RequiredPermission = 0, Status = ToolPermissionGrantStatus.Active };
        var rows = new List<ToolPermissionGrantRecord> { grant };
        var db = DispatchProxy.Create<IAuraDbContext, PermissionContextProxy>();
        var proxy = (PermissionContextProxy)(object)db;
        proxy.Grants = new CheckDbSet<ToolPermissionGrantRecord>(rows);
        proxy.Users = new CheckDbSet<User>([user]);
        proxy.Conversations = new CheckDbSet<Conversation>([conversation]);
        proxy.Projects = new CheckDbSet<Project>([project]);
        var source = new ToolPermissionPersistenceSource(db);
        var positive = await source.GetAsync(request, default);
        check(positive.Status == ToolPermissionLookupStatus.Found &&
            positive.Grant == new ToolPermissionGrant(grant.Id, scope, toolId!,
                ToolPermissionRequirement.OwnedProjectRead, true, false) && proxy.Saves == 0,
            "13B exact active grant maps to bounded application result without write");
        check(proxy.GrantReads == 1 && proxy.UserReads == 1 &&
            proxy.ConversationReads == 1 && proxy.ProjectReads == 1,
            "13B one bounded lookup composes grant and ownership integrity");
        foreach (var negative in new[] {
            request with { Scope = scope with { UserId = Guid.NewGuid() } },
            request with { Scope = scope with { ConversationId = Guid.NewGuid() } },
            request with { Scope = scope with { ProjectId = Guid.NewGuid() } },
            request with { Requirement = ToolPermissionRequirement.OwnedProjectWrite } })
            check((await source.GetAsync(negative, default)).Status == ToolPermissionLookupStatus.NotFound,
                "13B foreign exact user, conversation, project or requirement denied");
        ToolIdentifier.TryCreate("other-tool", out var otherTool);
        check((await source.GetAsync(request with { ToolId = otherTool! }, default)).Status ==
            ToolPermissionLookupStatus.NotFound, "13B foreign exact tool denied");
        check((await source.GetAsync(request with { Requirement = (ToolPermissionRequirement)999 }, default)).Status ==
            ToolPermissionLookupStatus.NotFound, "13B unknown requested requirement denied before query");
        check((await source.GetAsync(request with { Scope = scope with { UserId = Guid.Empty } }, default)).Status ==
            ToolPermissionLookupStatus.NotFound, "13B empty scope denied before query");

        user.IsActive = false;
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B inactive user denied");
        user.IsActive = true;
        conversation.UserId = Guid.NewGuid();
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B foreign conversation owner denied");
        conversation.UserId = userId;
        conversation.ProjectId = Guid.NewGuid();
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B conversation-project mismatch denied");
        conversation.ProjectId = projectId;
        conversation.Type = "General";
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B non-project conversation denied");
        conversation.Type = "Project";
        project.UserId = Guid.NewGuid();
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B project owner mismatch denied");
        project.UserId = userId;

        grant.Status = ToolPermissionGrantStatus.Revoked;
        grant.RevokedAt = DateTime.UtcNow;
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B revoked grant denied");
        grant.Status = ToolPermissionGrantStatus.Active;
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B active grant with RevokedAt denied");
        grant.RevokedAt = null;
        grant.ToolId = "bad*tool";
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B malformed persisted tool ID cannot authorize");
        grant.ToolId = toolId.Value;
        grant.RequiredPermission = 999;
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.NotFound,
            "13B unknown persisted permission numeric value cannot authorize");
        grant.RequiredPermission = 0;
        grant.Id = Guid.Empty;
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.Unavailable,
            "13B empty persisted grant ID fails closed");
        grant.Id = Guid.NewGuid();
        rows.Add(new ToolPermissionGrantRecord { Id = Guid.NewGuid(), UserId = userId,
            ConversationId = conversationId, ProjectId = projectId, ToolId = toolId.Value,
            RequiredPermission = 0, Status = ToolPermissionGrantStatus.Active });
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.Unavailable,
            "13B duplicate exact active rows cannot be hidden by a first match");
        rows.RemoveAt(1);

        var beforeError = proxy.GrantReads;
        proxy.FailRead = true;
        check((await source.GetAsync(request, default)).Status == ToolPermissionLookupStatus.Unavailable &&
            proxy.GrantReads == beforeError + 1 && proxy.Saves == 0,
            "13B database failure is unavailable without retry, fallback or write");
        proxy.FailRead = false;
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var propagated = false;
            try { await source.GetAsync(request, cancelled.Token); }
            catch (OperationCanceledException) { propagated = true; }
            check(propagated, "13B cancellation propagates before query");
        }

        var root = Directory.GetCurrentDirectory();
        var sourceText = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Data",
            "ToolPermissionPersistenceSource.cs"));
        check(sourceText.Contains("AsNoTracking()", StringComparison.Ordinal) &&
            sourceText.Contains(".Take(2)", StringComparison.Ordinal) &&
            !sourceText.Contains("FirstOrDefault", StringComparison.Ordinal) &&
            !sourceText.Contains("SaveChanges", StringComparison.Ordinal),
            "13B adapter is no-tracking, bounded at two, and has no write/first-match path");
        check(!sourceText.Contains("ResourceType", StringComparison.Ordinal) &&
            !sourceText.Contains("ResourceIdentifier", StringComparison.Ordinal) &&
            !sourceText.Contains("Role", StringComparison.Ordinal) &&
            !sourceText.Contains("ApprovalId", StringComparison.Ordinal),
            "13B no generic permission, role, owner or approval bypass mapping");
        var program = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        check(!program.Contains("ToolPermissionPersistenceSource", StringComparison.Ordinal) &&
            !program.Contains("AddScoped<IToolPermissionSource", StringComparison.Ordinal) &&
            !new UnavailableRuntimeCapabilityReadiness().GetSnapshot().Permission &&
            !new PersistenceCapabilityReadiness().GetSnapshot().Permission,
            "13B production adapter unregistered and both permission readiness values false");
        check(!new RuntimeSafetyOptions().ToolExecutionEnabled &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().SafeFileAccessEnabled,
            "13B tool, production and Safe File Access flags remain disabled");
        check(!Directory.GetFiles(Path.Combine(root, "Aura.Infrastructure", "Data", "Migrations"),
            "*ModelSnapshot.cs").Any(),
            "13B EF model snapshot/baseline remains absent and requires manual review");
        check(!File.ReadAllText(Path.Combine(root, "security-checks", "LivePermissionVerification.cs"))
                .Contains(".MigrateAsync(", StringComparison.Ordinal) &&
            !File.ReadAllText(Path.Combine(root, "security-checks", "LivePermissionVerification.cs"))
                .Contains(".SaveChangesAsync(", StringComparison.Ordinal),
            "13B live verifier never migrates, seeds or writes grants");
    }

    public class PermissionContextProxy : DispatchProxy
    {
        internal DbSet<ToolPermissionGrantRecord> Grants { get; set; } = null!;
        internal DbSet<User> Users { get; set; } = null!;
        internal DbSet<Conversation> Conversations { get; set; } = null!;
        internal DbSet<Project> Projects { get; set; } = null!;
        internal bool FailRead { get; set; }
        internal int GrantReads { get; private set; }
        internal int UserReads { get; private set; }
        internal int ConversationReads { get; private set; }
        internal int ProjectReads { get; private set; }
        internal int Saves { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_ToolPermissionGrants" => ReadGrants(),
            "get_Users" => ReadUsers(),
            "get_Conversations" => ReadConversations(),
            "get_Projects" => ReadProjects(),
            "SaveChangesAsync" => Save(),
            _ => throw new InvalidOperationException("Unexpected context operation.")
        };
        private DbSet<ToolPermissionGrantRecord> ReadGrants()
        { GrantReads++; if (FailRead) throw new InvalidOperationException("Synthetic DB failure."); return Grants; }
        private DbSet<User> ReadUsers() { UserReads++; return Users; }
        private DbSet<Conversation> ReadConversations() { ConversationReads++; return Conversations; }
        private DbSet<Project> ReadProjects() { ProjectReads++; return Projects; }
        private Task<int> Save() { Saves++; return Task.FromResult(1); }
    }
}
