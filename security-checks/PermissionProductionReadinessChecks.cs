using Aura.Api.Security;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

internal static class PermissionProductionReadinessChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuraDbContext, AuraDbContextFactory>(options =>
            options.UseNpgsql("Host=127.0.0.1;Database=aura_disposable_test;Username=fixture",
                npgsql => npgsql.UseVector()));
        services.AddScoped<IAuraDbContext>(
            OwnershipRuntimeFoundationRegistration.ResolveProductionDbContext);
        services.AddPermissionRuntimeFoundation();
        check(Exact<IToolPermissionSource, ToolPermissionPersistenceSource>(services) &&
            Exact<IToolPermissionPolicy, ExactToolPermissionPolicy>(services) &&
            Exact<IToolExecutionPermissionValidator, ToolExecutionPermissionValidator>(services),
            "17W exact source, policy and validator are each registered once as scoped");
        check(!ProductionRuntimeCompositionInspector.Inspect(services).Permission,
            "17W exact permission graph without authenticated ownership remains false");
        var production = new ServiceCollection();
        production.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        production.AddDbContextFactory<AuraDbContext, AuraDbContextFactory>(options =>
            options.UseNpgsql("Host=127.0.0.1;Database=aura_disposable_test;Username=fixture",
                npgsql => npgsql.UseVector()));
        production.AddScoped<IAuraDbContext>(
            OwnershipRuntimeFoundationRegistration.ResolveProductionDbContext);
        production.AddOwnershipRuntimeFoundation();
        production.AddPermissionRuntimeFoundation();
        var ready = ProductionRuntimeCompositionInspector.Inspect(production);
        check(ready.Ownership && ready.Permission && !ready.ExecutionState && !ready.AuditSink &&
            !ready.AuditDelivery && !ready.Reconciliation && !ready.Verification &&
            !ready.ExecutionPolicy && !ready.ToolHandlers && !ready.TrustedExecution &&
            !ready.CompositionComplete,
            "17W exact graph and reviewed live revocation evidence establish Permission");
        var duplicate = new ServiceCollection();
        foreach (var item in services) ((ICollection<ServiceDescriptor>)duplicate).Add(item);
        duplicate.AddScoped<IToolPermissionSource, UnavailableToolPermissionSource>();
        check(!ProductionRuntimeCompositionInspector.Inspect(duplicate).Permission,
            "17W fallback mixed with exact permission source cannot become ready");
        var missingValidator = new ServiceCollection();
        foreach (var item in services.Where(x =>
            x.ServiceType != typeof(IToolExecutionPermissionValidator)))
            ((ICollection<ServiceDescriptor>)missingValidator).Add(item);
        check(!ProductionRuntimeCompositionInspector.Inspect(missingValidator).Permission,
            "17W missing permission validator cannot become ready");
        check(services.Count(x => x.ServiceType == typeof(IToolPermissionSource)) == 1 &&
            services.Count(x => x.ServiceType == typeof(IToolPermissionPolicy)) == 1 &&
            services.Count(x => x.ServiceType == typeof(IToolExecutionPermissionValidator)) == 1 &&
            !services.Any(x => x.ImplementationType == typeof(UnavailableToolPermissionSource)),
            "17W production graph has no duplicate, fallback or fixture permission node");
        var missingSource = new ServiceCollection();
        foreach (var item in services.Where(x => x.ServiceType != typeof(IToolPermissionSource)))
            ((ICollection<ServiceDescriptor>)missingSource).Add(item);
        check(!missingSource.Any(x => x.ServiceType == typeof(IToolPermissionSource)) &&
            !ProductionRuntimeCompositionInspector.Inspect(missingSource).Permission,
            "17W missing permission source cannot become ready");
        var wrongLifetime = new ServiceCollection();
        foreach (var item in services.Where(x => x.ServiceType != typeof(IToolPermissionSource)))
            ((ICollection<ServiceDescriptor>)wrongLifetime).Add(item);
        wrongLifetime.AddSingleton<IToolPermissionSource, ToolPermissionPersistenceSource>();
        check(!Exact<IToolPermissionSource, ToolPermissionPersistenceSource>(wrongLifetime) &&
            !ProductionRuntimeCompositionInspector.Inspect(wrongLifetime).Permission,
            "17W wrong permission source lifetime cannot become ready");
        check(duplicate.Count(x => x.ServiceType == typeof(IToolPermissionSource)) == 2 &&
            !ProductionRuntimeCompositionInspector.Inspect(duplicate).Permission,
            "17W duplicate permission source cannot become ready");

        var root = Directory.GetCurrentDirectory();
        var inspector = File.ReadAllText(Path.Combine(root, "Aura.Api", "Security",
            "ProductionRuntimeCompositionInspector.cs"));
        var grant = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Data",
            "Configurations", "ToolPermissionGrantRecordConfiguration.cs"));
        var source = File.ReadAllText(Path.Combine(root, "Aura.Infrastructure", "Data",
            "ToolPermissionPersistenceSource.cs"));
        var validator = File.ReadAllText(Path.Combine(root, "Aura.Application", "Executions",
            "ToolExecutionPermissionValidator.cs"));
        check(inspector.Contains("ReviewedPermissionPolicy") &&
            inspector.Contains("ReviewedPermissionValidator") &&
            inspector.Contains("HasRequiredPermissionLiveEvidence()") &&
            inspector.Contains("ReviewedPermissionLiveEvidence =") &&
            inspector.Contains("PermissionEvidenceResult = \"PASS\"") &&
            grant.Contains("UX_ToolPermissionGrants_ActiveExactGrant"),
            "17W exact reviewed catalog records live revocation PASS and active duplicate is schema-prevented");
        check(source.Contains("grant.Status == ToolPermissionGrantStatus.Active && grant.RevokedAt == null") &&
            source.Contains(".AsNoTracking()") && source.Contains(".Take(2)") &&
            validator.Contains("source.GetAsync(request, cancellationToken)") &&
            !validator.Contains("IMemoryCache") && !validator.Contains("ConcurrentDictionary"),
            "17W each decision reads current exact active non-revoked database grant without cache");
        check(!grant.Contains("ExpiresAt") && !source.Contains("ExpiresAt"),
            "17W grant expiry is absent from the reviewed model");

        var output = new List<string>();
        check(await LivePermissionRevocationEvidence.RunAsync(_ => null, output.Add) == 2 &&
            output.SequenceEqual(["PRODUCTION PERMISSION REVOCATION: SKIPPED", "RESULT: INCOMPLETE"]),
            "17W absent live gates skip before connection access");
        output.Clear();
        check(await LivePermissionRevocationEvidence.RunAsync(key => key switch
            {
                LivePermissionRevocationEvidence.Enable => "true",
                LivePermissionRevocationEvidence.Disposable => "true",
                _ => null
            }, output.Add) == 2 &&
            output.SequenceEqual(["INPUT: FAIL", "RESULT: INCOMPLETE"]),
            "17W live command requires verified disposable target identity");
    }

    private static bool Exact<TContract, TImplementation>(IServiceCollection services) =>
        services.Count(x => x.ServiceType == typeof(TContract) &&
            x.ImplementationType == typeof(TImplementation) &&
            x.Lifetime == ServiceLifetime.Scoped) == 1;
}
