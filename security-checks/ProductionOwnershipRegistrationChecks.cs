using Aura.Api.Security;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

internal static class ProductionOwnershipRegistrationChecks
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.GetCurrentDirectory();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts]));
        var api = Read("Aura.Api", "Program.cs");
        var current = Read("Aura.Api", "Services", "HttpCurrentUserContext.cs");
        var mapping = Read("Aura.Infrastructure", "Data", "AuthenticatedUserMappingSource.cs");
        var owned = Read("Aura.Infrastructure", "Data", "OwnershipPersistenceSource.cs");
        var inspector = Read("Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs");
        check(typeof(ICurrentUserContext).IsAssignableFrom(typeof(Aura.Api.Services.HttpCurrentUserContext)) &&
            typeof(IOwnershipSource).IsAssignableFrom(typeof(OwnershipPersistenceSource)) &&
            typeof(IToolExecutionScopeValidator).IsAssignableFrom(typeof(ExactOwnershipScopeValidator)),
            "17B exact ownership contracts and concrete implementations exist");
        check(api.Contains("options.MapInboundClaims = false") &&
            current.Contains("principal.FindAll(\"sub\")") &&
            current.Contains("subjects.Length != 1") &&
            mapping.Contains("user.AuthUserId == authUserId && user.IsActive") &&
            owned.Contains("c.UserId == request.UserId") &&
            owned.Contains("c.ProjectId == request.ProjectId") &&
            owned.Contains("p.UserId == request.UserId"),
            "17B raw subject, active local mapping and exact owned chain remain fail closed");
        check(!api.Contains("AddScoped<ICurrentUserContext") &&
            !api.Contains("AddScoped<IOwnershipSource") &&
            !api.Contains("AddScoped<IToolExecutionScopeValidator") &&
            inspector.Contains("ReviewedOwnershipContext = new HashSet<Type>()") &&
            !ProductionRuntimeCompositionInspector.Inspect(new ServiceCollection()).Ownership,
            "17B incomplete evidence leaves production registration and catalog blocked");
        return Task.CompletedTask;
    }
}
