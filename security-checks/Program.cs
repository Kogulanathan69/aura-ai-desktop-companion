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

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}

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
await ProductionAdapterReadinessChecks.RunAsync(Check);
await RuntimeSafetyChecks.RunAsync(Check);
await PersistenceFoundationChecks.RunAsync(Check);
await CurrentUserRuntimeChecks.RunAsync(Check);

var defaults = new ConfigurationBuilder().Build().GetSection(ProjectFileAccessOptions.SectionName)
    .Get<ProjectFileAccessOptions>() ?? new();
Check(!defaults.Enabled, "missing configuration disables access");
var user = Guid.NewGuid();
var disabled = new ProjectFileAccessOptions { AllowedRootsByUser = new() { [user.ToString("D")] = ["invalid"] } };
var service = new SafeProjectFileContentService(null!, null!, null!, null!, disabled);
Check((await service.GetAsync(Guid.NewGuid(), Guid.NewGuid())).Status == ProjectFileContentStatus.Unavailable,
    "disabled service touches no identity/database/privacy/reader dependency");
Check((await new ProjectFileContentReader(disabled).ReadAsync(user, null, null!, null!, null!)).Status ==
    ProjectFileContentStatus.Unavailable, "disabled reader returns before resolving invalid paths and roots");

foreach (var enabled in new[] { false, true })
{
    var builder = WebApplication.CreateBuilder();
    builder.Services.AddSingleton(new ProjectFileAccessOptions { Enabled = enabled });
    builder.Services.AddScoped<ISafeProjectFileContentService>(_ => service);
    await using var app = builder.Build();
    app.MapProjectFileContentEndpoints();
    var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
    Check(routes.Length == (enabled ? 1 : 0), $"endpoint mapping Enabled={enabled}");
    if (enabled) Check(routes.Single().Metadata.GetMetadata<IAuthorizeData>() is not null,
        "enabled endpoint requires authorization");
}

if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Physical checks require Windows.");
var scratch = Path.Combine(Path.GetTempPath(), "AuraSecurityChecks-" + Guid.NewGuid().ToString("N"));
var project = Path.Combine(scratch, "App");
Directory.CreateDirectory(project);
var options = new ProjectFileAccessOptions
{
    Enabled = true, AllowedRootsByUser = new() { [user.ToString("D")] = [scratch] }
};
var reader = new ProjectFileContentReader(options);
async Task<ProjectFileReadResult> Read(string name, string? root = null, Guid? caller = null) =>
    await reader.ReadAsync(caller ?? user, root ?? project, name, name.Replace('\\', '/').Split('/').Last(), Path.GetExtension(name));

await File.WriteAllTextAsync(Path.Combine(project, "a.txt"), "hello", new UTF8Encoding(false));
Check((await Read("a.txt")).Content == "hello", "authorized allowed-root UTF-8 read");
Check((await Read("a.txt", caller: Guid.NewGuid())).Content is null, "other user's roots cannot authorize");
Check((await Read("../a.txt")).Content is null, "traversal rejected");
Check((await Read(Path.Combine(project, "a.txt"))).Content is null, "absolute injection rejected");
Check((await Read("a.txt", @"\\server\share\App")).Content is null, "UNC project rejected");
Check((await Read("a.txt", Path.GetPathRoot(project))).Content is null, "volume root rejected");
var collision = scratch + "Secrets";
Check((await Read("a.txt", collision)).Content is null, "directory-prefix collision rejected");
await File.WriteAllBytesAsync(Path.Combine(project, "binary.txt"), [65, 0, 66]);
Check((await Read("binary.txt")).Status == ProjectFileContentStatus.UnsafeOrUnreadable, "NUL rejected");
await File.WriteAllBytesAsync(Path.Combine(project, "invalid.txt"), [0xff, 0xfe]);
Check((await Read("invalid.txt")).Status == ProjectFileContentStatus.UnsafeOrUnreadable, "invalid UTF-8 rejected");
await File.WriteAllBytesAsync(Path.Combine(project, "large.txt"), new byte[ProjectFileAccessOptions.MaximumBytes + 1]);
Check((await Read("large.txt")).Status == ProjectFileContentStatus.TooLarge, "limit plus one rejected");
await File.WriteAllBytesAsync(Path.Combine(project, "limit.txt"), Enumerable.Repeat((byte)'a', ProjectFileAccessOptions.MaximumBytes).ToArray());
Check((await Read("limit.txt")).Content?.Length == ProjectFileAccessOptions.MaximumBytes, "exact 1 MiB accepted");
await File.WriteAllTextAsync(Path.Combine(project, "a.exe"), "text");
Check((await Read("a.exe")).Status == ProjectFileContentStatus.UnsupportedType, "unsupported extension rejected");
Check((await Read("missing.txt")).Status == ProjectFileContentStatus.UnsafeOrUnreadable, "missing file safe failure");
var malformed = new ProjectFileContentReader(new ProjectFileAccessOptions
{
    Enabled = true, AllowedRootsByUser = new() { [user.ToString("D")] = [scratch, "relative"] }
});
Check((await malformed.ReadAsync(user, project, "a.txt", "a.txt", ".txt")).Content is null,
    "malformed configured root fails closed even alongside valid root");
var guard = new PrivacyGuard();
Check(guard.Evaluate(new(".env", ".env", "")).Decision == Aura.Application.Privacy.Models.PrivacyDecision.Block,
    "sensitive metadata blocks");
Check(guard.Evaluate(new("a.txt", "a.txt", ".txt", "password=secret")).RedactedContent == "password=[REDACTED]",
    "credential redaction produces sanitized text");
Check(guard.Evaluate(new("a.txt", "a.txt", ".txt", "-----BEGIN PRIVATE KEY-----")).Decision ==
    Aura.Application.Privacy.Models.PrivacyDecision.Block, "private-key content blocks");
// Keep the isolated fixture path for inspection; no recursive deletion of filesystem test data.
Console.WriteLine($"{checks} checks passed. Temporary fixture: {scratch}");
