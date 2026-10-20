using System.Reflection;
using Aura.Api.Security;
using Aura.Application.Common.Interfaces;
using Aura.Application.Verifications;
using Aura.Infrastructure.Data;
using Aura.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class VerificationProductionRegistrationChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        const string connection = "Host=127.0.0.1;Database=offline_registration;Username=fixture";
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuraDbContext, AuraDbContextFactory>(options =>
            options.UseNpgsql(connection, provider => provider.UseVector()));
        services.AddScoped<IAuraDbContext>(provider => provider.GetRequiredService<AuraDbContext>());
        services.AddScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>();
        services.AddScoped<IExecutionVerificationAssessmentWriter,
            ExecutionVerificationAssessmentPersistenceWriter>();

        var factoryRegistration = services.SingleOrDefault(x =>
            x.ServiceType == typeof(IDbContextFactory<AuraDbContext>));
        check(factoryRegistration?.Lifetime == ServiceLifetime.Singleton &&
            factoryRegistration.ImplementationType == typeof(AuraDbContextFactory) &&
            services.Count(x => x.ServiceType == typeof(AuraDbContext)) == 1 &&
            services.Single(x => x.ServiceType == typeof(AuraDbContext)).Lifetime == ServiceLifetime.Scoped &&
            services.Single(x => x.ServiceType == typeof(IAuraDbContext)).Lifetime == ServiceLifetime.Scoped,
            "17U one reviewed singleton factory supplies one scoped context and interface alias");
        check(ExactScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>(services) &&
            ExactScoped<IExecutionVerificationAssessmentWriter,
                ExecutionVerificationAssessmentPersistenceWriter>(services),
            "17U exact source and writer are unique scoped registrations");

        await using (var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        }))
        {
            var factory = provider.GetRequiredService<IDbContextFactory<AuraDbContext>>();
            await using var created = await factory.CreateDbContextAsync();
            using var scope = provider.CreateScope();
            var scoped = scope.ServiceProvider.GetRequiredService<AuraDbContext>();
            var alias = scope.ServiceProvider.GetRequiredService<IAuraDbContext>();
            var source = scope.ServiceProvider.GetRequiredService<IExecutionEvidenceSource>();
            var writer = scope.ServiceProvider.GetRequiredService<IExecutionVerificationAssessmentWriter>();
            check(ReferenceEquals(scoped, alias) && !ReferenceEquals(scoped, created) &&
                created.Database.ProviderName == scoped.Database.ProviderName &&
                created.Database.GetConnectionString() == connection &&
                scoped.Database.GetConnectionString() == connection &&
                source is OwnedProjectMetadataExecutionEvidenceSource &&
                writer is ExecutionVerificationAssessmentPersistenceWriter,
                "17U validated scopes resolve the same Npgsql configuration without DB access");
        }

        var inspector = typeof(ProductionRuntimeCompositionInspector);
        static IReadOnlySet<Type>? Catalog(Type owner, string name) =>
            owner.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                as IReadOnlySet<Type>;
        check(Catalog(inspector, "ReviewedVerification")?.SetEquals(
                [typeof(OwnedProjectMetadataExecutionEvidenceSource)]) == true &&
            Catalog(inspector, "ReviewedVerificationWriter")?.SetEquals(
                [typeof(ExecutionVerificationAssessmentPersistenceWriter)]) == true &&
            Catalog(inspector, "ReviewedVerificationFactory")?.SetEquals(
                [typeof(AuraDbContextFactory)]) == true &&
            Catalog(inspector, "ReviewedVerificationWorkflow")?.Count == 0 &&
            Catalog(inspector, "ReviewedVerificationPolicy")?.Count == 0,
            "17U catalog names only proven foundations; workflow and policy are pending");
        check(!ProductionRuntimeCompositionInspector.Inspect(services).Verification &&
            !ProductionRuntimeCompositionInspector.Inspect(services).CompositionComplete &&
            !new RuntimeSafetyOptions().ProductionAdaptersEnabled &&
            !new RuntimeSafetyOptions().ToolExecutionEnabled &&
            !new UnavailableFinalProductionSecurityApprovalSource().IsApproved,
            "17U partial graph cannot assert readiness or activation");

        var api = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Aura.Api", "Program.cs"));
        check(api.Contains("AddDbContextFactory<AuraDbContext, AuraDbContextFactory>") &&
            !api.Contains("AddDbContext<AuraDbContext>(") &&
            api.Contains("AddScoped<IAuraDbContext>(") &&
            api.Contains("AddScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>()") &&
            api.Contains("AddScoped<IExecutionVerificationAssessmentWriter,") &&
            !api.Contains("AddScoped<IVerificationWorkflowService") &&
            !api.Contains("MigrateAsync(") && !api.Contains("EnsureCreated("),
            "17U API has one database options path and no incomplete workflow or startup migration");

        var duplicate = new ServiceCollection();
        duplicate.AddScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>();
        duplicate.AddScoped<IExecutionEvidenceSource, UnavailableExecutionEvidenceSource>();
        var wrongLifetime = new ServiceCollection();
        wrongLifetime.AddSingleton<IExecutionVerificationAssessmentWriter,
            ExecutionVerificationAssessmentPersistenceWriter>();
        var wrongSourceLifetime = new ServiceCollection();
        wrongSourceLifetime.AddSingleton<IExecutionEvidenceSource,
            OwnedProjectMetadataExecutionEvidenceSource>();
        var duplicateWriter = new ServiceCollection();
        duplicateWriter.AddScoped<IExecutionVerificationAssessmentWriter,
            ExecutionVerificationAssessmentPersistenceWriter>();
        duplicateWriter.AddScoped<IExecutionVerificationAssessmentWriter,
            ExecutionVerificationAssessmentPersistenceWriter>();
        check(!ExactScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>(duplicate) &&
            !ExactScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>(wrongSourceLifetime) &&
            !ExactScoped<IExecutionVerificationAssessmentWriter,
                ExecutionVerificationAssessmentPersistenceWriter>(wrongLifetime) &&
            !ExactScoped<IExecutionVerificationAssessmentWriter,
                ExecutionVerificationAssessmentPersistenceWriter>(duplicateWriter),
            "17U duplicate/fallback or wrong-lifetime source/writer violate reviewed shape");

        static ServiceCollection CopyWithout(IServiceCollection original, Type omitted)
        {
            var copy = new ServiceCollection();
            foreach (var item in original.Where(x => x.ServiceType != omitted))
                ((IServiceCollection)copy).Add(item);
            return copy;
        }
        var noFactory = CopyWithout(services, typeof(IDbContextFactory<AuraDbContext>));
        var noSource = CopyWithout(services, typeof(IExecutionEvidenceSource));
        var noWriter = CopyWithout(services, typeof(IExecutionVerificationAssessmentWriter));
        check(noFactory.All(x => x.ServiceType != typeof(IDbContextFactory<AuraDbContext>)) &&
            !ExactScoped<IExecutionEvidenceSource, OwnedProjectMetadataExecutionEvidenceSource>(noSource) &&
            !ExactScoped<IExecutionVerificationAssessmentWriter,
                ExecutionVerificationAssessmentPersistenceWriter>(noWriter) &&
            !ProductionRuntimeCompositionInspector.Inspect(noFactory).Verification &&
            !ProductionRuntimeCompositionInspector.Inspect(noSource).Verification &&
            !ProductionRuntimeCompositionInspector.Inspect(noWriter).Verification,
            "17U missing factory, source or writer cannot satisfy composition");

        var incompleteWorkflow = new ServiceCollection();
        foreach (var item in services) ((IServiceCollection)incompleteWorkflow).Add(item);
        incompleteWorkflow.AddScoped<IVerificationWorkflowService, VerificationWorkflowService>();
        incompleteWorkflow.AddScoped<IIndependentVerificationPolicy, ConservativeVerificationPolicy>();
        var unresolved = false;
        try
        {
            using var invalid = incompleteWorkflow.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true, ValidateOnBuild = true
            });
        }
        catch (AggregateException) { unresolved = true; }
        check(unresolved &&
            incompleteWorkflow.All(x => x.ServiceType != typeof(IVerificationScopeValidator)) &&
            incompleteWorkflow.All(x => x.ServiceType != typeof(Aura.Application.Tools.ToolRegistry)) &&
            !ProductionRuntimeCompositionInspector.Inspect(incompleteWorkflow).Verification,
            "17U workflow cannot resolve without production scope validator and tool registry");

        var inspectorSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
            "Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs"));
        check(inspectorSource.Contains("HasExactVerificationSource(services)") &&
            inspectorSource.Contains("HasExactVerificationWriter(services)") &&
            inspectorSource.Contains("HasExactVerificationWorkflow(services)") &&
            inspectorSource.Contains("HasExactVerificationFactory(services)") &&
            inspectorSource.Contains("HasExactVerificationPolicy(services)") &&
            inspectorSource.Contains("HasOneScoped<IVerificationScopeValidator>(services)") &&
            inspectorSource.Contains("HasOneScoped<ToolRegistry>(services)"),
            "17U inspector requires every verification dependency and exact reviewed catalog");
    }

    private static bool ExactScoped<TContract, TImplementation>(IServiceCollection services)
        where TContract : class where TImplementation : class, TContract
    {
        var registrations = services.Where(x => x.ServiceType == typeof(TContract)).ToArray();
        return registrations.Length == 1 &&
            registrations[0].Lifetime == ServiceLifetime.Scoped &&
            registrations[0].ImplementationType == typeof(TImplementation);
    }
}
