using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

// Test executable only. The ephemeral key and loopback host never enter API config.
internal static class LiveSignedJwtOwnershipEvidence
{
    internal const string Command = "--live-signed-jwt-ownership-evidence";
    private const string Enable = "AURA_LIVE_PRODUCTION_OWNERSHIP_EVIDENCE";
    private const string Disposable = "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE";
    private const string Connection = "ConnectionStrings__DefaultConnection";
    private const string ExpectedOid = "AURA_EXPECTED_DATABASE_OID";
    private const string ExpectedSystem = "AURA_EXPECTED_SYSTEM_IDENTIFIER";
    private const string LoopbackNonTls = "AURA_ALLOW_LOOPBACK_NON_TLS_DISPOSABLE";

    internal static async Task<int> RunAsync(Func<string, string?> environment,
        Action<string> write, CancellationToken token = default)
    {
        if (environment(Enable) != "true" || environment(Disposable) != "true")
        {
            write("LIVE SIGNED JWT OWNERSHIP: SKIPPED");
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
            foreach (var (name, passed) in stages)
                write(name + ": " + (passed ? "PASS" : "FAIL"));
            var success = stages.Count > 0 && stages.All(x => x.Passed);
            write(success ? "RESULT: PASS" : "RESULT: INCOMPLETE");
            return success ? 0 : 2;
        }
        catch (Exception)
        {
            // Never print a DB connection, bearer token, key or exception.
            write("RESULT: INCOMPLETE");
            return 2;
        }
    }

    private static async Task<List<(string Name, bool Passed)>> ExecuteAsync(string connection,
        uint expectedOid, long expectedSystem, CancellationToken token)
    {
        var results = new List<(string Name, bool Passed)>();
        void Check(string name, bool passed) => results.Add((name, passed));
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
                return results;
            }
        }
        Check("DB CONNECTIVITY", true);
        try
        {
            await db.Users.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(token);
            await db.Projects.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(token);
            await db.Conversations.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(token);
            await db.AIActions.AsNoTracking().Select(x => x.Id).Take(1).ToArrayAsync(token);
            Check("SCHEMA CHECK", true);
        }
        catch (Exception) { Check("SCHEMA CHECK", false); return results; }

        var now = DateTime.UtcNow;
        var owner = new User { Id = Guid.NewGuid(), AuthUserId = Guid.NewGuid(), IsActive = true,
            Email = Guid.NewGuid().ToString("N") + "@example.invalid", DisplayName = "ownership fixture",
            CreatedAt = now, UpdatedAt = now };
        var foreign = new User { Id = Guid.NewGuid(), AuthUserId = Guid.NewGuid(), IsActive = true,
            Email = Guid.NewGuid().ToString("N") + "@example.invalid", DisplayName = "foreign fixture",
            CreatedAt = now, UpdatedAt = now };
        Project Project(User user) => new() { Id = Guid.NewGuid(), UserId = user.Id,
            Name = "ownership fixture", CreatedAt = now, UpdatedAt = now };
        var project = Project(owner);
        var foreignProject = Project(foreign);
        Conversation Conversation(User user, Project p) => new() { Id = Guid.NewGuid(),
            UserId = user.Id, ProjectId = p.Id, Type = "Project", Title = "ownership fixture",
            CreatedAt = now, UpdatedAt = now };
        var conversation = Conversation(owner, project);
        var foreignConversation = Conversation(foreign, foreignProject);
        AIAction Action(User user, Project p, Conversation c) => new() { Id = Guid.NewGuid(),
            UserId = user.Id, ProjectId = p.Id, ConversationId = c.Id,
            ActionType = "Evidence", Title = "ownership fixture", Description = "fixture",
            CreatedAt = now, UpdatedAt = now };
        var action = Action(owner, project, conversation);
        var foreignAction = Action(foreign, foreignProject, foreignConversation);
        db.Users.AddRange(owner, foreign);
        db.Projects.AddRange(project, foreignProject);
        db.Conversations.AddRange(conversation, foreignConversation);
        db.AIActions.AddRange(action, foreignAction);
        try
        {
            await db.SaveChangesAsync(token);
            Check("FIXTURE SETUP", true);
        }
        catch (Exception) { Check("FIXTURE SETUP", false); return results; }

        try
        {
            await using var host = await StartHostAsync(connection, token);
            using var client = new HttpClient { BaseAddress = host.Address };
            var bearer = new JwtSecurityTokenHandler();
            string Sign(Guid subject) => bearer.WriteToken(new JwtSecurityToken(
                issuer: host.Issuer, audience: host.Audience,
                claims: [new Claim("sub", subject.ToString("D"))],
                notBefore: now.AddMinutes(-1), expires: now.AddMinutes(10),
                signingCredentials: new SigningCredentials(host.Key,
                    SecurityAlgorithms.HmacSha256)));
            async Task<HttpStatusCode> Get(string path, string? jwt)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                if (jwt is not null) request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", jwt);
                using var response = await client.SendAsync(request, token);
                return response.StatusCode;
            }
            string Path(Guid p, Guid c, Guid a) => $"/owned/{p:D}/{c:D}/{a:D}";
            var valid = Sign(owner.AuthUserId);
            Check("SIGNED JWT ACCEPT", await Get("/whoami", valid) == HttpStatusCode.OK);
            Check("JWT SUBJECT MAP", await Get($"/whoami/{owner.Id:D}", valid) == HttpStatusCode.OK);
            Check("OWNED PROJECT ALLOW", await Get(Path(project.Id, conversation.Id, action.Id), valid) == HttpStatusCode.OK);
            Check("FOREIGN PROJECT DENY", await Get(Path(foreignProject.Id, conversation.Id, action.Id), valid) == HttpStatusCode.Forbidden);
            Check("UNKNOWN PROJECT DENY", await Get(Path(Guid.NewGuid(), conversation.Id, action.Id), valid) == HttpStatusCode.Forbidden);
            Check("OWNED CONVERSATION ALLOW", await Get(Path(project.Id, conversation.Id, action.Id), valid) == HttpStatusCode.OK);
            Check("FOREIGN CONVERSATION DENY", await Get(Path(project.Id, foreignConversation.Id, action.Id), valid) == HttpStatusCode.Forbidden);
            Check("PROJECT CONVERSATION MISMATCH DENY", await Get(Path(foreignProject.Id, conversation.Id, action.Id), valid) == HttpStatusCode.Forbidden);
            Check("OWNED ACTION ALLOW", await Get(Path(project.Id, conversation.Id, action.Id), valid) == HttpStatusCode.OK);
            Check("FOREIGN ACTION DENY", await Get(Path(project.Id, conversation.Id, foreignAction.Id), valid) == HttpStatusCode.Forbidden);
            Check("ACTION CONVERSATION MISMATCH DENY", await Get(Path(project.Id, foreignConversation.Id, action.Id), valid) == HttpStatusCode.Forbidden);
            Check("ACTION PROJECT MISMATCH DENY", await Get(Path(foreignProject.Id, conversation.Id, action.Id), valid) == HttpStatusCode.Forbidden);
            Check("ACTION USER CHAIN MISMATCH DENY", await Get(Path(foreignProject.Id, foreignConversation.Id, foreignAction.Id), valid) == HttpStatusCode.Forbidden);
            Check("UNAUTHENTICATED DENY", await Get("/whoami", null) == HttpStatusCode.Unauthorized);
            Check("MALFORMED TOKEN DENY", await Get("/whoami", "malformed") == HttpStatusCode.Unauthorized);
            Check("UNKNOWN SUBJECT DENY", await Get("/whoami", Sign(Guid.NewGuid())) == HttpStatusCode.Forbidden);
            // Users.AuthUserId has a unique index; ambiguity cannot be fixture-created.
            Check("NEGATIVE BINDINGS", results.Where(x => x.Name.EndsWith("DENY", StringComparison.Ordinal))
                .All(x => x.Passed));
        }
        finally
        {
            db.AIActions.RemoveRange(action, foreignAction);
            db.Conversations.RemoveRange(conversation, foreignConversation);
            db.Projects.RemoveRange(project, foreignProject);
            db.Users.RemoveRange(owner, foreign);
            await db.SaveChangesAsync(CancellationToken.None);
            Check("CLEANUP", !await db.Users.AsNoTracking().AnyAsync(u =>
                u.Id == owner.Id || u.Id == foreign.Id, CancellationToken.None));
        }
        return results;
    }

    private sealed record Host(WebApplication App, Uri Address, string Issuer,
        string Audience, SymmetricSecurityKey Key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await App.DisposeAsync();
    }

    private static async Task<Host> StartHostAsync(string connection, CancellationToken token)
    {
        var issuer = "aura-disposable-ownership-" + Guid.NewGuid().ToString("N");
        const string audience = "aura-disposable-ownership-evidence";
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = issuer,
                    ValidateAudience = true, ValidAudience = audience,
                    ValidateIssuerSigningKey = true, IssuerSigningKey = key,
                    ValidateLifetime = true, ClockSkew = TimeSpan.Zero
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddDbContext<AuraDbContext>(options =>
            options.UseNpgsql(connection, npgsql => npgsql.UseVector()));
        builder.Services.AddScoped<IAuraDbContext>(
            OwnershipRuntimeFoundationRegistration.ResolveProductionDbContext);
        builder.Services.AddOwnershipRuntimeFoundation();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/whoami", async (ICurrentUserContext current, CancellationToken ct) =>
            (await current.GetAsync(ct)).Status == CurrentUserStatus.Authenticated
                ? Results.Ok() : Results.Forbid()).RequireAuthorization();
        app.MapGet("/whoami/{expected:guid}", async (Guid expected, ICurrentUserContext current,
            CancellationToken ct) => (await current.GetAsync(ct)).UserId == expected
                ? Results.Ok() : Results.Forbid()).RequireAuthorization();
        app.MapGet("/owned/{project:guid}/{conversation:guid}/{action:guid}",
            async (Guid project, Guid conversation, Guid action, IAuraDbContext db,
                ICurrentUserContext current, IExactActionOwnershipValidator ownership,
                CancellationToken ct) =>
            {
                var user = await current.GetAsync(ct);
                if (user.Status != CurrentUserStatus.Authenticated) return Results.Forbid();
                var row = await db.AIActions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == action, ct);
                if (row is null || row.ConversationId is null || row.ProjectId is null)
                    return Results.Forbid();
                ToolIdentifier.TryCreate("owned-project-metadata-read", out var tool);
                var descriptor = new ActionDescriptor(ActionIdentifier.RestoreTrusted(row.Id),
                    new ActionScope(row.UserId, row.ConversationId.Value, row.ProjectId.Value),
                    tool!, "evidence", ActionLifecycleStatus.Approved, row.CreatedAt, row.UpdatedAt);
                var exact = ExactActionOwnershipScope.FromTrustedAction(descriptor);
                return descriptor.Scope == new ActionScope(user.UserId, conversation, project) &&
                    await ownership.ValidateActionAsync(exact, ct)
                    ? Results.Ok() : Results.Forbid();
            }).RequireAuthorization();
        await app.StartAsync(token);
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault();
        if (address is null) throw new InvalidOperationException("Loopback host unavailable.");
        return new Host(app, new Uri(address), issuer, audience, key);
    }
}
