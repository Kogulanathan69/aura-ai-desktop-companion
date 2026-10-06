using Aura.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Aura.Application.Common.Interfaces;
using Aura.Application.Common.Services;
using Aura.Application.Projects.Interfaces;
using Aura.Application.Projects.Services;
using Aura.Application.ProjectSessions.Interfaces;
using Aura.Application.ProjectSessions.Services;
using Aura.Application.ProjectFiles.Interfaces;
using Aura.Application.ProjectFiles.Services;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.ProjectMemories.Interfaces;
using Aura.Application.ProjectMemories.Services;
using Aura.Application.Conversations.Interfaces;
using Aura.Application.Conversations.Services;
using Aura.Application.ProjectContext.Interfaces;
using Aura.Application.ProjectContext.Services;
using Aura.Application.Privacy.Interfaces;
using Aura.Application.Privacy.Services;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Aura.Application.AI.Interfaces;
using Aura.Application.AI.Chat.Interfaces;
using Aura.Application.AI.Chat.Services;
using Aura.Infrastructure.AI.Ollama;
using Aura.Application.AI.Providers;
using Aura.Infrastructure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Aura.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
}

var jwtAuthority = builder.Configuration["Supabase:Authority"];
var jwtAudience = builder.Configuration["Supabase:Audience"];
if (!Uri.TryCreate(jwtAuthority, UriKind.Absolute, out var authorityUri) ||
    authorityUri.Scheme != Uri.UriSchemeHttps ||
    string.IsNullOrWhiteSpace(jwtAudience))
{
    throw new InvalidOperationException(
        "Supabase:Authority must be an HTTPS URL and Supabase:Audience is required.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtAuthority;
        options.Audience = jwtAudience;
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorization();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

// OpenAPI
builder.Services.AddOpenApi();

// HTTP Context
builder.Services.AddHttpContextAccessor();

// Application Services
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IProjectSessionService, ProjectSessionService>();
builder.Services.AddScoped<IProjectFileService, ProjectFileService>();
builder.Services.AddScoped<IProjectMemoryService, ProjectMemoryService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IAiChatService, AiChatService>();
// Startup-bound server configuration; no client or runtime toggle.
builder.Services.AddSingleton(builder.Configuration.GetSection(ProjectFileAccessOptions.SectionName)
    .Get<ProjectFileAccessOptions>() ?? new ProjectFileAccessOptions());
builder.Services.AddScoped<ISafeProjectFileContentService, SafeProjectFileContentService>();
builder.Services.AddSingleton<IProjectFileContentReader, ProjectFileContentReader>();
builder.Services.AddScoped<IProjectContextService, ProjectContextService>();
builder.Services.AddSingleton<IPrivacyGuard, PrivacyGuard>();
builder.Services.AddScoped<IUserIdentityService, UserIdentityService>();

// Current User
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Infrastructure Services
builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
// Server-only startup snapshot; provider validates it again before every request.
var ollamaOptions = builder.Configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new();
if (ollamaOptions.Enabled && !OllamaConfiguration.TryValidate(ollamaOptions, out _))
    throw new InvalidOperationException("Local AI configuration is invalid.");
builder.Services.AddSingleton(ollamaOptions);
builder.Services.AddHttpClient<ILocalAiProvider, OllamaAiProvider>(client =>
    client.Timeout = TimeSpan.FromSeconds(120))
    .ConfigurePrimaryHttpMessageHandler(OllamaConfiguration.CreateHandler)
    .RemoveAllLoggers();

// Independent cloud foundation; no chat orchestration consumes this registration.
var openAiOptions = builder.Configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new();
if (openAiOptions.Enabled && !OpenAiConfiguration.TryValidate(openAiOptions))
    throw new InvalidOperationException("Cloud AI configuration is invalid.");
builder.Services.AddSingleton(openAiOptions);
builder.Services.AddHttpClient<ICloudAiProvider, OpenAiProvider>(client =>
    client.Timeout = TimeSpan.FromSeconds(120))
    .ConfigurePrimaryHttpMessageHandler(OpenAiConfiguration.CreateHandler)
    .RemoveAllLoggers();

// Database
builder.Services.AddDbContext<AuraDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.UseVector()));

builder.Services.AddScoped<IAuraDbContext>(provider =>
    provider.GetRequiredService<AuraDbContext>());

var app = builder.Build();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    Application = "AURA API",
    Status = "Running"
}));

app.MapProjectEndpoints();
app.MapProjectSessionEndpoints();
app.MapProjectFileEndpoints();
app.MapProjectFileContentEndpoints();
app.MapProjectContextEndpoints();
app.MapProjectMemoryEndpoints();
app.MapConversationEndpoints();
app.MapAiChatEndpoints();

app.Run();
