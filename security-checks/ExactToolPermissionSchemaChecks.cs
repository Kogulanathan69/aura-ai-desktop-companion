using Aura.Api.Security;
using Aura.Application.Executions;
using Aura.Application.Security;
using Aura.Application.Tools;
using Aura.Domain.Entities;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

internal static class ExactToolPermissionSchemaChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new DbContextOptionsBuilder<AuraDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only", npgsql => npgsql.UseVector()).Options;
        await using var db = new AuraDbContext(options);
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var grant = designModel.FindEntityType(typeof(ToolPermissionGrantRecord))!;
        var generic = db.Model.FindEntityType(typeof(Permission))!;
        var fields = grant.GetProperties().Select(property => property.Name).ToHashSet();
        var constraints = grant.GetCheckConstraints().ToDictionary(item => item.Name, item => item.Sql);
        check(grant.GetTableName() == "ToolPermissionGrants" &&
            db.ToolPermissionGrants is not null && grant.FindPrimaryKey()!.Properties.Single().Name == "Id",
            "13A dedicated exact tool-permission table and key exist");
        check(grant.FindProperty("Id")!.ClrType == typeof(Guid) &&
            constraints["CK_ToolPermissionGrants_NonemptyIds"].Contains("\"Id\" <>"),
            "13A grant ID is a nonempty UUID by database constraint");
        foreach (var name in new[] { "UserId", "ConversationId", "ProjectId" })
            check(grant.FindProperty(name) is { IsNullable: false } &&
                constraints["CK_ToolPermissionGrants_NonemptyIds"].Contains("\"" + name + "\" <>"),
                "13A exact required nonempty scope: " + name);
        check(grant.FindProperty("ToolId") is { IsNullable: false } toolId &&
            toolId.GetMaxLength() == ToolLimits.MaximumIdCharacters &&
            constraints["CK_ToolPermissionGrants_ToolId"].Contains("^[a-z][a-z0-9-]{0,63}$"),
            "13A tool ID is required, canonical and bounded to 64 characters");
        check(grant.FindProperty("RequiredPermission") is { IsNullable: false } requirement &&
            requirement.ClrType == typeof(int) &&
            constraints["CK_ToolPermissionGrants_RequiredPermission"].Contains("IN (0, 1, 2)") &&
            (int)ToolPermissionRequirement.OwnedProjectRead == 0 &&
            (int)ToolPermissionRequirement.OwnedProjectWrite == 1 &&
            (int)ToolPermissionRequirement.ExternalAction == 2,
            "13A required permission has reviewed constrained numeric enum values");
        check(grant.FindProperty("Status")!.ClrType == typeof(ToolPermissionGrantStatus) &&
            grant.FindProperty("Status")!.GetColumnType() == "integer" &&
            constraints["CK_ToolPermissionGrants_Status"].Contains("\"RevokedAt\" IS NULL") &&
            constraints["CK_ToolPermissionGrants_Status"].Contains("\"RevokedAt\" IS NOT NULL"),
            "13A active and revoked status have exact timestamp semantics");
        check(grant.FindProperty("RevokedAt")!.IsNullable &&
            !grant.FindProperty("CreatedAt")!.IsNullable &&
            !grant.FindProperty("GrantedAt")!.IsNullable,
            "13A grant timestamps are explicit and revocation timestamp is conditional");
        check(!fields.Any(name => name is "ResourceType" or "ResourceIdentifier" or "AccessLevel" or
                "Role" or "ApprovalId") &&
            typeof(ToolPermissionGrantRecord).GetProperties().All(property =>
                property.Name is not "ResourceType" and not "ResourceIdentifier" and not "AccessLevel"),
            "13A no generic resource, role, or approval bypass field in exact grant");
        check(generic.GetTableName() == "Permissions" &&
            generic.FindProperty("ResourceType") is not null &&
            generic.FindProperty("ResourceIdentifier") is not null &&
            generic.FindProperty("AccessLevel") is not null &&
            generic.FindProperty("ProjectId")!.IsNullable,
            "13A generic project-file Permission semantics remain separate");
        foreach (var principal in new[] { typeof(User), typeof(Conversation), typeof(Project) })
            check(grant.GetForeignKeys().Any(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == principal &&
                foreignKey.Properties.Single().IsNullable == false &&
                foreignKey.DeleteBehavior == DeleteBehavior.Restrict),
                "13A required Restrict foreign key to " + principal.Name);
        check(grant.GetForeignKeys().Count() == 3 &&
            !grant.GetForeignKeys().Any(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.SetNull),
            "13A exact scope cannot silently become partial on delete");
        check(grant.GetIndexes().Any(index => index.IsUnique &&
            index.GetDatabaseName() == "UX_ToolPermissionGrants_ActiveExactGrant" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                "UserId", "ConversationId", "ProjectId", "ToolId", "RequiredPermission"]) &&
            index.GetFilter() == "\"Status\" = 1 AND \"RevokedAt\" IS NULL"),
            "13A exact active duplicates are prevented; revoked history may repeat");
        check(!grant.GetSeedData().Any(), "13A no automatic grant seeding");

        var root = Directory.GetCurrentDirectory();
        var migration = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Data", "Migrations",
            "20261014000000_AddToolPermissionGrants.cs"));
        check(migration.Contains("CreateTable(", StringComparison.Ordinal) &&
            migration.Contains("name: \"ToolPermissionGrants\"", StringComparison.Ordinal) &&
            migration.Contains("DropTable(name: \"ToolPermissionGrants\")", StringComparison.Ordinal),
            "13A additive migration creates and reverses only exact grant table");
        check(!migration.Contains("AlterTable(", StringComparison.Ordinal) &&
            !migration.Contains("DropColumn(", StringComparison.Ordinal) &&
            !migration.Contains("DropTable(name: \"Permissions\"", StringComparison.Ordinal) &&
            !migration.Contains("InsertData(", StringComparison.Ordinal) &&
            !migration.Contains("Sql(", StringComparison.Ordinal),
            "13A migration does not alter generic permissions, copy rows, or seed grants");
        check(migration.Contains("ReferentialAction.Restrict", StringComparison.Ordinal) &&
            migration.Contains("UX_ToolPermissionGrants_ActiveExactGrant", StringComparison.Ordinal),
            "13A migration carries reviewed scope FKs and active uniqueness");
        var migrationOperations = new AddToolPermissionGrants();
        check(migrationOperations.UpOperations.Count(operation => operation is CreateTableOperation) == 1 &&
            migrationOperations.UpOperations.All(operation => operation is CreateTableOperation or CreateIndexOperation) &&
            migrationOperations.DownOperations.Count == 1 &&
            migrationOperations.DownOperations.Single() is DropTableOperation drop &&
            drop.Name == "ToolPermissionGrants",
            "13A compiled migration only creates exact grant objects and drops that table on rollback");
        var createdTable = migrationOperations.UpOperations.OfType<CreateTableOperation>().Single();
        check(createdTable.Columns.Select(column => column.Name).ToHashSet().SetEquals(fields) &&
            createdTable.ForeignKeys.Count == 3 && createdTable.CheckConstraints.Count == 4 &&
            createdTable.ForeignKeys.All(foreignKey => foreignKey.OnDelete ==
                Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict),
            "13A compiled migration matches exact model fields and restrictive scope constraints");

        var program = File.ReadAllText(Path.Combine(root, "Aura.Api", "Program.cs"));
        check(!program.Contains("AddScoped<IToolPermissionSource", StringComparison.Ordinal) &&
            !program.Contains("AddSingleton<IToolPermissionSource", StringComparison.Ordinal) &&
            !program.Contains("AddTransient<IToolPermissionSource", StringComparison.Ordinal),
            "13A no production tool-permission source registration");
        var startup = new UnavailableRuntimeCapabilityReadiness().GetSnapshot();
        var informational = new PersistenceCapabilityReadiness().GetSnapshot();
        var safety = new RuntimeSafetyOptions();
        check(!startup.Permission && !informational.Permission &&
            !safety.ProductionAdaptersEnabled && !safety.ToolExecutionEnabled &&
            !safety.SafeFileAccessEnabled,
            "13A schema presence does not enable runtime permission or tool/file access");
        check(typeof(AuraDbContext).Assembly.GetTypes()
                .Concat(typeof(IToolPermissionSource).Assembly.GetTypes())
                .Where(type => type.IsClass && !type.IsAbstract &&
                    typeof(IToolPermissionSource).IsAssignableFrom(type))
                .ToHashSet().SetEquals([typeof(UnavailableToolPermissionSource),
                    typeof(ToolPermissionPersistenceSource)]),
            "13A exact source is unregistered; generic permission remains separate");
    }
}
