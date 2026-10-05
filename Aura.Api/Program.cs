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
using Aura.Application.ProjectContext.Interfaces;
using Aura.Application.ProjectContext.Services;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
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
builder.Services.AddScoped<IProjectContextService, ProjectContextService>();
builder.Services.AddScoped<IUserIdentityService, UserIdentityService>();

// Current User
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Infrastructure Services
builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

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
app.MapProjectContextEndpoints();

app.Run();
