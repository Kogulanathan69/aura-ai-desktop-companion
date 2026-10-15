using System.Text;
using Aura.Api.Endpoints;
using Aura.Application.Privacy.Services;
using Aura.Application.ProjectFiles.Content;
using Aura.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args.Length == 1 && args[0] == LiveOwnershipVerification.Command)
{
    Environment.ExitCode = await LiveOwnershipVerification.RunAsync(
        Environment.GetEnvironmentVariable,
        Console.WriteLine);

    return;
}

if (args.Length == 1 && args[0] == LivePermissionVerification.Command)
{
    Environment.ExitCode = await LivePermissionVerification.RunAsync(
        Environment.GetEnvironmentVariable,
        Console.WriteLine);

    return;
}

if (args.Length == 1 && args[0] == LiveExecutionStateVerification.Command)
{
    Environment.ExitCode = await LiveExecutionStateVerification.RunAsync(
        Environment.GetEnvironmentVariable,
        Console.WriteLine);

    return;
}

if (args.Length == 1 && args[0] == LiveAuditOutboxVerification.Command)
{
    Environment.ExitCode = await LiveAuditOutboxVerification.RunAsync(
        Environment.GetEnvironmentVariable, Console.WriteLine);
    return;
}

if (args.Length == 1 && args[0] == LiveAuditOutboxHardeningVerification.Command)
{
    Environment.ExitCode = await LiveAuditOutboxHardeningVerification.RunAsync(
        Environment.GetEnvironmentVariable, Console.WriteLine);
    return;
}

var checks = 0;

void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(name);
    }

    checks++;

    Console.WriteLine($"PASS {name}");
}


// --------------------------------------------------
// Core checks
// --------------------------------------------------

await UpdateChecks.RunAsync(Check);
await MemoryChecks.RunAsync(Check);
await ConversationChecks.RunAsync(Check);

await OllamaChecks.RunAsync(Check);
await AiChatChecks.RunAsync(Check);
await OpenAiChecks.RunAsync(Check);
await RouterChecks.RunAsync(Check);
await RetrievalChecks.RunAsync(Check);

await ToolChecks.RunAsync(Check);
await ActionChecks.RunAsync(Check);
await ApprovalChecks.RunAsync(Check);
await VerificationChecks.RunAsync(Check);
await ExecutionChecks.RunAsync(Check);


// --------------------------------------------------
// Production / runtime readiness
// --------------------------------------------------

await ProductionAdapterReadinessChecks.RunAsync(Check);
await RuntimeSafetyChecks.RunAsync(Check);
await PersistenceFoundationChecks.RunAsync(Check);

await CurrentUserRuntimeChecks.RunAsync(Check);
await AuthenticatedUserMappingChecks.RunAsync(Check);
await UserIdentityHardeningChecks.RunAsync(Check);

await OwnershipRuntimeRegistrationChecks.RunAsync(Check);
await LiveOwnershipVerificationChecks.RunAsync(Check);


// --------------------------------------------------
// Persistence readiness
// --------------------------------------------------

await PermissionPersistenceReadinessChecks.RunAsync(Check);
await ExecutionStatePersistenceReadinessChecks.RunAsync(Check);
await AuditPersistenceReadinessChecks.RunAsync(Check);
await ReconciliationVerificationPersistenceReadinessChecks.RunAsync(Check);

await RuntimeReadinessConsolidationChecks.RunAsync(Check);
await ProductionRuntimeCompositionChecks.RunAsync(Check);
await ProductionOwnershipCompositionChecks.RunAsync(Check);
await ProductionPermissionCompositionChecks.RunAsync(Check);
await ProductionExecutionStateCompositionChecks.RunAsync(Check);
await ProductionAuditSinkCompositionChecks.RunAsync(Check);
await ProductionAuditDeliveryCompositionChecks.RunAsync(Check);
await ProductionReconciliationCompositionChecks.RunAsync(Check);
await FinalProductionReadinessChecks.RunAsync(Check);
await FinalProductionActivationGateChecks.RunAsync(Check);


// --------------------------------------------------
// Permission / execution persistence
// --------------------------------------------------

await ExactToolPermissionSchemaChecks.RunAsync(Check);
await ToolPermissionPersistenceChecks.RunAsync(Check);
await LivePermissionVerificationChecks.RunAsync(Check);

await ExecutionStateAtomicStoreChecks.RunAsync(Check);
await LiveExecutionStateVerificationChecks.RunAsync(Check);


// --------------------------------------------------
// Step 13E
// --------------------------------------------------

await SecurityPersistenceBundleChecks.RunAsync(Check);


// --------------------------------------------------
// Step 13F-A
// Persistence hardening without live PostgreSQL
// --------------------------------------------------

await MigrationBaselineReadinessChecks.RunAsync(Check);
await CompositeBindingIntegrityChecks.RunAsync(Check);
await AuditDeliveryReliabilityChecks.RunAsync(Check);
await PersistenceHardeningReadinessChecks.RunAsync(Check);


// --------------------------------------------------
// Step 13G
// Security Audit Outbox Foundation
// --------------------------------------------------

await SecurityAuditOutboxChecks.RunAsync(Check);
await SecurityAuditOutboxDispatcherChecks.RunAsync(Check);
await LiveAuditOutboxVerificationChecks.RunAsync(Check);
await AuditOutboxSchemaHardeningChecks.RunAsync(Check);
await LiveAuditOutboxHardeningVerificationChecks.RunAsync(Check);


// --------------------------------------------------
// Safe File Access checks
// --------------------------------------------------

var defaults = new ConfigurationBuilder()
    .Build()
    .GetSection(ProjectFileAccessOptions.SectionName)
    .Get<ProjectFileAccessOptions>()
    ?? new();

Check(
    !defaults.Enabled,
    "missing configuration disables access");

var user = Guid.NewGuid();

var disabled = new ProjectFileAccessOptions
{
    AllowedRootsByUser = new()
    {
        [user.ToString("D")] = ["invalid"]
    }
};

var service = new SafeProjectFileContentService(
    null!,
    null!,
    null!,
    null!,
    disabled);

Check(
    (await service.GetAsync(
        Guid.NewGuid(),
        Guid.NewGuid()))
        .Status == ProjectFileContentStatus.Unavailable,
    "disabled service touches no identity/database/privacy/reader dependency");

Check(
    (await new ProjectFileContentReader(disabled)
        .ReadAsync(
            user,
            null,
            null!,
            null!,
            null!))
        .Status == ProjectFileContentStatus.Unavailable,
    "disabled reader returns before resolving invalid paths and roots");


// --------------------------------------------------
// Endpoint mapping checks
// --------------------------------------------------

foreach (var enabled in new[] { false, true })
{
    var builder = WebApplication.CreateBuilder();

    builder.Services.AddSingleton(
        new ProjectFileAccessOptions
        {
            Enabled = enabled
        });

    builder.Services.AddScoped<ISafeProjectFileContentService>(
        _ => service);

    await using var app =
        builder.Build();

    app.MapProjectFileContentEndpoints();

    var routes =
        ((IEndpointRouteBuilder)app)
        .DataSources
        .SelectMany(
            source => source.Endpoints)
        .ToArray();

    Check(
        routes.Length == (enabled ? 1 : 0),
        $"endpoint mapping Enabled={enabled}");

    if (enabled)
    {
        Check(
            routes
                .Single()
                .Metadata
                .GetMetadata<IAuthorizeData>()
                is not null,
            "enabled endpoint requires authorization");
    }
}


// --------------------------------------------------
// Physical file checks
// --------------------------------------------------

if (!OperatingSystem.IsWindows())
{
    throw new InvalidOperationException(
        "Physical checks require Windows.");
}

var scratch =
    Path.Combine(
        Path.GetTempPath(),
        "AuraSecurityChecks-" +
        Guid.NewGuid().ToString("N"));

var project =
    Path.Combine(
        scratch,
        "App");

Directory.CreateDirectory(project);

var options =
    new ProjectFileAccessOptions
    {
        Enabled = true,

        AllowedRootsByUser = new()
        {
            [user.ToString("D")] =
            [
                scratch
            ]
        }
    };

var reader =
    new ProjectFileContentReader(options);

async Task<ProjectFileReadResult> Read(
    string name,
    string? root = null,
    Guid? caller = null)
{
    return await reader.ReadAsync(
        caller ?? user,
        root ?? project,
        name,
        name
            .Replace('\\', '/')
            .Split('/')
            .Last(),
        Path.GetExtension(name));
}


// --------------------------------------------------
// Valid UTF-8 file
// --------------------------------------------------

await File.WriteAllTextAsync(
    Path.Combine(
        project,
        "a.txt"),
    "hello",
    new UTF8Encoding(false));

Check(
    (await Read("a.txt"))
        .Content == "hello",
    "authorized allowed-root UTF-8 read");


// --------------------------------------------------
// User isolation
// --------------------------------------------------

Check(
    (await Read(
        "a.txt",
        caller: Guid.NewGuid()))
        .Content is null,
    "other user's roots cannot authorize");


// --------------------------------------------------
// Path traversal
// --------------------------------------------------

Check(
    (await Read("../a.txt"))
        .Content is null,
    "traversal rejected");


// --------------------------------------------------
// Absolute path injection
// --------------------------------------------------

Check(
    (await Read(
        Path.Combine(
            project,
            "a.txt")))
        .Content is null,
    "absolute injection rejected");


// --------------------------------------------------
// UNC path
// --------------------------------------------------

Check(
    (await Read(
        "a.txt",
        @"\\server\share\App"))
        .Content is null,
    "UNC project rejected");


// --------------------------------------------------
// Volume root
// --------------------------------------------------

Check(
    (await Read(
        "a.txt",
        Path.GetPathRoot(project)))
        .Content is null,
    "volume root rejected");


// --------------------------------------------------
// Prefix collision
// --------------------------------------------------

var collision =
    scratch + "Secrets";

Check(
    (await Read(
        "a.txt",
        collision))
        .Content is null,
    "directory-prefix collision rejected");


// --------------------------------------------------
// NUL content
// --------------------------------------------------

await File.WriteAllBytesAsync(
    Path.Combine(
        project,
        "binary.txt"),
    [65, 0, 66]);

Check(
    (await Read("binary.txt"))
        .Status ==
    ProjectFileContentStatus.UnsafeOrUnreadable,
    "NUL rejected");


// --------------------------------------------------
// Invalid UTF-8
// --------------------------------------------------

await File.WriteAllBytesAsync(
    Path.Combine(
        project,
        "invalid.txt"),
    [0xff, 0xfe]);

Check(
    (await Read("invalid.txt"))
        .Status ==
    ProjectFileContentStatus.UnsafeOrUnreadable,
    "invalid UTF-8 rejected");


// --------------------------------------------------
// File size limit + 1
// --------------------------------------------------

await File.WriteAllBytesAsync(
    Path.Combine(
        project,
        "large.txt"),
    new byte[
        ProjectFileAccessOptions.MaximumBytes + 1]);

Check(
    (await Read("large.txt"))
        .Status ==
    ProjectFileContentStatus.TooLarge,
    "limit plus one rejected");


// --------------------------------------------------
// Exact file size limit
// --------------------------------------------------

await File.WriteAllBytesAsync(
    Path.Combine(
        project,
        "limit.txt"),
    Enumerable
        .Repeat(
            (byte)'a',
            ProjectFileAccessOptions.MaximumBytes)
        .ToArray());

Check(
    (await Read("limit.txt"))
        .Content?.Length ==
    ProjectFileAccessOptions.MaximumBytes,
    "exact 1 MiB accepted");


// --------------------------------------------------
// Unsupported extension
// --------------------------------------------------

await File.WriteAllTextAsync(
    Path.Combine(
        project,
        "a.exe"),
    "text");

Check(
    (await Read("a.exe"))
        .Status ==
    ProjectFileContentStatus.UnsupportedType,
    "unsupported extension rejected");


// --------------------------------------------------
// Missing file
// --------------------------------------------------

Check(
    (await Read("missing.txt"))
        .Status ==
    ProjectFileContentStatus.UnsafeOrUnreadable,
    "missing file safe failure");


// --------------------------------------------------
// Malformed configured root
// --------------------------------------------------

var malformed =
    new ProjectFileContentReader(
        new ProjectFileAccessOptions
        {
            Enabled = true,

            AllowedRootsByUser = new()
            {
                [user.ToString("D")] =
                [
                    scratch,
                    "relative"
                ]
            }
        });

Check(
    (await malformed.ReadAsync(
        user,
        project,
        "a.txt",
        "a.txt",
        ".txt"))
        .Content is null,
    "malformed configured root fails closed even alongside valid root");


// --------------------------------------------------
// Privacy guard checks
// --------------------------------------------------

var guard =
    new PrivacyGuard();

Check(
    guard.Evaluate(
        new(
            ".env",
            ".env",
            ""))
        .Decision ==
    Aura.Application.Privacy.Models.PrivacyDecision.Block,
    "sensitive metadata blocks");

Check(
    guard.Evaluate(
        new(
            "a.txt",
            "a.txt",
            ".txt",
            "password=secret"))
        .RedactedContent ==
    "password=[REDACTED]",
    "credential redaction produces sanitized text");

Check(
    guard.Evaluate(
        new(
            "a.txt",
            "a.txt",
            ".txt",
            "-----BEGIN PRIVATE KEY-----"))
        .Decision ==
    Aura.Application.Privacy.Models.PrivacyDecision.Block,
    "private-key content blocks");


// Keep the isolated fixture path for inspection.
// No recursive deletion of filesystem test data.

Console.WriteLine(
    $"{checks} checks passed. Temporary fixture: {scratch}");
