using System.Globalization;
using Aura.Application.Actions;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// Explicit, disposable-target-only write harness. It never runs in the offline suite.
internal static class LivePermissionRevocationEvidence
{
    internal const string Command = "--live-production-permission-revocation-evidence";
    internal const string Enable = "AURA_LIVE_PRODUCTION_PERMISSION_EVIDENCE";
    internal const string Disposable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";
    internal const string Connection = "ConnectionStrings__DefaultConnection";
    internal const string ExpectedOid = "AURA_EXPECTED_DATABASE_OID";
    internal const string ExpectedSystem = "AURA_EXPECTED_SYSTEM_IDENTIFIER";
    internal const string LoopbackNonTls = "AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE";

    internal static async Task<int> RunAsync(Func<string, string?> environment, Action<string> write,
        CancellationToken token = default)
    {
        if (environment(Enable) != "true" || environment(Disposable) != "true")
        {
            write("PRODUCTION PERMISSION REVOCATION: SKIPPED");
            write("RESULT: INCOMPLETE");
            return 2;
        }
        var connection = environment(Connection);
        if (!uint.TryParse(environment(ExpectedOid), NumberStyles.None,
                CultureInfo.InvariantCulture, out var oid) || oid == 0 ||
            !long.TryParse(environment(ExpectedSystem), NumberStyles.None,
                CultureInfo.InvariantCulture, out var system) || system <= 0 ||
            !LiveProjectVersionMigrationApplication.ValidConnection(connection, true,
                environment(LoopbackNonTls) == "true"))
        {
            write("INPUT: FAIL");
            write("RESULT: INCOMPLETE");
            return 2;
        }
        try
        {
            var stages = await ExecuteAsync(connection!, oid, system, token);
            foreach (var (name, stagePassed) in stages)
                write(name + ": " + (stagePassed ? "PASS" : "FAIL"));
            var passed = stages.Count > 0 && stages.All(x => x.Passed);
            write(passed ? "RESULT: PASS" : "RESULT: INCOMPLETE");
            return passed ? 0 : 2;
        }
        catch (Exception)
        {
            write("RESULT: INCOMPLETE");
            return 2;
        }
    }

    private static async Task<List<(string Name, bool Passed)>> ExecuteAsync(string connection,
        uint expectedOid, long expectedSystem, CancellationToken token)
    {
        var stages = new List<(string Name, bool Passed)>();
        void Check(string name, bool passed) => stages.Add((name, passed));
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.UseVector()).Options;
        await using var db = new AuraDbContext(options);
        await db.Database.OpenConnectionAsync(token);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT current_database(), " +
                "(SELECT oid FROM pg_database WHERE datname=current_database()), " +
                "(SELECT system_identifier FROM pg_control_system())";
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) ||
                !LiveProjectVersionMigrationApplication.MatchesIdentity(reader.GetString(0),
                    Convert.ToUInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
                    expectedOid, expectedSystem))
            {
                Check("DB CONNECTIVITY", false);
                return stages;
            }
        }
        Check("DB CONNECTIVITY", true);
        try
        {
            await db.ToolPermissionGrants.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(token);
            await db.Users.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(token);
            await db.Projects.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(token);
            await db.Conversations.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(token);
            Check("SCHEMA CHECK", true);
        }
        catch (Exception) { Check("SCHEMA CHECK", false); return stages; }

        var now = DateTime.UtcNow;
        User User() => new() { Id = Guid.NewGuid(), AuthUserId = Guid.NewGuid(),
            DisplayName = "permission fixture", Email = Guid.NewGuid().ToString("N") + "@example.invalid",
            IsActive = true, CreatedAt = now, UpdatedAt = now };
        var owner = User(); var foreign = User();
        Project Project(User user) => new() { Id = Guid.NewGuid(), UserId = user.Id,
            Name = "permission fixture", CreatedAt = now, UpdatedAt = now };
        var project = Project(owner); var foreignProject = Project(foreign);
        Conversation Conversation(User user, Project p) => new() { Id = Guid.NewGuid(),
            UserId = user.Id, ProjectId = p.Id, Type = "Project", Title = "permission fixture",
            CreatedAt = now, UpdatedAt = now };
        var conversation = Conversation(owner, project);
        var foreignConversation = Conversation(foreign, foreignProject);
        ToolIdentifier.TryCreate("owned-project-metadata-read", out var toolId);
        ToolIdentifier.TryCreate("wrong-project-tool", out var wrongToolId);
        var tool = new ToolDescriptor(toolId!, "Evidence", "Disposable evidence",
            ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead);
        var wrongTool = new ToolDescriptor(wrongToolId!, "Wrong", "Disposable evidence",
            ToolCapability.ProjectRead, ToolPermissionRequirement.OwnedProjectRead);
        var wrongPermission = new ToolDescriptor(toolId!, "Wrong", "Disposable evidence",
            ToolCapability.ProjectWrite, ToolPermissionRequirement.OwnedProjectWrite);
        var grant = new ToolPermissionGrantRecord { Id = Guid.NewGuid(), UserId = owner.Id,
            ConversationId = conversation.Id, ProjectId = project.Id, ToolId = toolId!.Value,
            RequiredPermission = (int)ToolPermissionRequirement.OwnedProjectRead,
            Status = ToolPermissionGrantStatus.Active, CreatedAt = now, GrantedAt = now };
        db.Users.AddRange(owner, foreign);
        db.Projects.AddRange(project, foreignProject);
        db.Conversations.AddRange(conversation, foreignConversation);
        db.ToolPermissionGrants.Add(grant);
        try { await db.SaveChangesAsync(token); Check("FIXTURE SETUP", true); }
        catch (Exception) { Check("FIXTURE SETUP", false); return stages; }

        try
        {
            var validator = new ToolExecutionPermissionValidator(
                new ToolPermissionPersistenceSource(db), new ExactToolPermissionPolicy());
            var scope = new ActionScope(owner.Id, conversation.Id, project.Id);
            async Task<bool> Allows(ActionScope value, ToolDescriptor descriptor) =>
                await validator.HasPermissionAsync(value, descriptor, token);
            var firstAllow = await Allows(scope, tool);
            Check("ACTIVE GRANT ALLOW", firstAllow);
            Check("OWNED USER GRANT ALLOW", firstAllow);
            Check("EXACT PROJECT GRANT ALLOW", firstAllow);
            Check("EXACT TOOL GRANT ALLOW", firstAllow);
            Check("EXACT PERMISSION ALLOW", firstAllow);
            Check("FOREIGN USER GRANT DENY", !await Allows(scope with { UserId = foreign.Id }, tool));
            Check("WRONG PROJECT DENY", !await Allows(scope with { ProjectId = foreignProject.Id }, tool));
            Check("WRONG CONVERSATION DENY", !await Allows(scope with { ConversationId = foreignConversation.Id }, tool));
            Check("WRONG TOOL DENY", !await Allows(scope, wrongTool));
            Check("WRONG PERMISSION DENY", !await Allows(scope, wrongPermission));
            Check("UNKNOWN GRANT DENY", !await Allows(new ActionScope(owner.Id,
                Guid.NewGuid(), project.Id), tool));
            // The persisted status and timestamp are the actual revocation transition.
            grant.Status = ToolPermissionGrantStatus.Revoked;
            grant.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
            var deniedAfterRevocation = !await Allows(scope, tool);
            Check("REVOKED GRANT DENY", deniedAfterRevocation);
            Check("REVOCATION AFTER PRIOR ALLOW DENY", firstAllow && deniedAfterRevocation);
            Check("NEGATIVE BINDINGS", stages.Where(x => x.Name.EndsWith("DENY",
                StringComparison.Ordinal)).All(x => x.Passed));
        }
        finally
        {
            db.ToolPermissionGrants.Remove(grant);
            db.Conversations.RemoveRange(conversation, foreignConversation);
            db.Projects.RemoveRange(project, foreignProject);
            db.Users.RemoveRange(owner, foreign);
            await db.SaveChangesAsync(CancellationToken.None);
            Check("CLEANUP", !await db.ToolPermissionGrants.AsNoTracking()
                .AnyAsync(x => x.Id == grant.Id, CancellationToken.None));
        }
        return stages;
    }
}
