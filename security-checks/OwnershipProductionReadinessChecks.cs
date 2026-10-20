using Aura.Api.Security;
using Aura.Api.Services;
using Aura.Application.Actions;
using Aura.Application.Common.Interfaces;
using Aura.Application.Executions;
using Aura.Application.Tools;
using Aura.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class OwnershipProductionReadinessChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAuraDbContext>(_ => throw new InvalidOperationException());
        services.AddOwnershipRuntimeFoundation();
        check(Exact<IAuthenticatedUserMappingSource, AuthenticatedUserMappingSource>(services) &&
            Exact<ICurrentUserContext, HttpCurrentUserContext>(services) &&
            Exact<IOwnershipSource, OwnershipPersistenceSource>(services) &&
            Exact<ExactOwnershipScopeValidator, ExactOwnershipScopeValidator>(services) &&
            services.Count(x => x.ServiceType == typeof(IExactActionOwnershipValidator) &&
                x.Lifetime == ServiceLifetime.Scoped) == 1 &&
            services.Count(x => x.ServiceType == typeof(IToolExecutionScopeValidator) &&
                x.Lifetime == ServiceLifetime.Scoped) == 1,
            "17V exact scoped production ownership graph has one registration per node");

        var inspector = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
            "Aura.Api", "Security", "ProductionRuntimeCompositionInspector.cs"));
        check(inspector.Contains("typeof(HttpCurrentUserContext)") &&
            inspector.Contains("typeof(AuthenticatedUserMappingSource)") &&
            inspector.Contains("typeof(OwnershipPersistenceSource)") &&
            inspector.Contains("typeof(ExactOwnershipScopeValidator)") &&
            inspector.Contains("ReviewedOwnershipLiveEvidence") &&
            inspector.Contains("HasRequiredOwnershipLiveEvidence()"),
            "17V exact catalog and reviewed signed-JWT evidence gate readiness");
        check(!ProductionRuntimeCompositionInspector.Inspect(services).Ownership &&
            !ProductionRuntimeCompositionInspector.Inspect(services).CompositionComplete,
            "17V graph without authenticated host and exact DB factory cannot claim Ownership");

        var production = new ServiceCollection();
        production.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        production.AddDbContextFactory<AuraDbContext, AuraDbContextFactory>(options =>
            options.UseNpgsql("Host=127.0.0.1;Database=aura_disposable_test;Username=fixture",
                npgsql => npgsql.UseVector()));
        production.AddScoped<IAuraDbContext>(
            OwnershipRuntimeFoundationRegistration.ResolveProductionDbContext);
        production.AddOwnershipRuntimeFoundation();
        var ready = ProductionRuntimeCompositionInspector.Inspect(production);
        check(ready.Ownership && !ready.Permission && !ready.ExecutionState &&
            !ready.AuditSink && !ready.AuditDelivery && !ready.Reconciliation &&
            !ready.Verification && !ready.ExecutionPolicy && !ready.ToolHandlers &&
            !ready.TrustedExecution && !ready.CompositionComplete,
            "17V exact authenticated graph and reviewed live evidence establish Ownership only");
        ServiceCollection Copy() { var copy = new ServiceCollection(); foreach (var item in production) ((ICollection<ServiceDescriptor>)copy).Add(item); return copy; }
        var missingIdentity = Copy();
        missingIdentity.Remove(missingIdentity.Single(x => x.ServiceType == typeof(ICurrentUserContext)));
        check(!ProductionRuntimeCompositionInspector.Inspect(missingIdentity).Ownership,
            "17V missing authenticated current-user context denies readiness");
        var missingBearer = Copy();
        missingBearer.Remove(missingBearer.Single(x => x.ServiceType == typeof(JwtBearerHandler)));
        check(!ProductionRuntimeCompositionInspector.Inspect(missingBearer).Ownership,
            "17V missing JWT bearer handler denies readiness");
        var missingMapping = Copy();
        missingMapping.Remove(missingMapping.Single(x =>
            x.ServiceType == typeof(IAuthenticatedUserMappingSource)));
        check(!ProductionRuntimeCompositionInspector.Inspect(missingMapping).Ownership,
            "17V missing subject-to-local-user mapping denies readiness");
        var missingSource = Copy();
        missingSource.Remove(missingSource.Single(x => x.ServiceType == typeof(IOwnershipSource)));
        check(!ProductionRuntimeCompositionInspector.Inspect(missingSource).Ownership,
            "17V missing ownership source denies readiness");
        var fakeDbAlias = Copy();
        fakeDbAlias.Remove(fakeDbAlias.Single(x => x.ServiceType == typeof(IAuraDbContext)));
        fakeDbAlias.AddScoped<IAuraDbContext>(_ => throw new InvalidOperationException());
        check(!ProductionRuntimeCompositionInspector.Inspect(fakeDbAlias).Ownership,
            "17V fake scoped database alias denies readiness");
        var missingValidator = Copy();
        missingValidator.Remove(missingValidator.Single(x => x.ServiceType == typeof(ExactOwnershipScopeValidator)));
        check(!ProductionRuntimeCompositionInspector.Inspect(missingValidator).Ownership,
            "17V missing exact validator denies readiness");
        var missingActionAlias = Copy();
        missingActionAlias.Remove(missingActionAlias.Single(x =>
            x.ServiceType == typeof(IExactActionOwnershipValidator)));
        check(!ProductionRuntimeCompositionInspector.Inspect(missingActionAlias).Ownership,
            "17V missing exact action validator alias denies readiness");
        var wrongAlias = Copy();
        var alias = wrongAlias.Single(x => x.ServiceType == typeof(IExactActionOwnershipValidator));
        wrongAlias.Remove(alias);
        wrongAlias.AddScoped<IExactActionOwnershipValidator>(_ => throw new InvalidOperationException());
        check(!ProductionRuntimeCompositionInspector.Inspect(wrongAlias).Ownership,
            "17V fallback or wrong validator alias denies readiness");
        var catalogMismatch = Copy();
        catalogMismatch.Remove(catalogMismatch.Single(x => x.ServiceType == typeof(IOwnershipSource)));
        catalogMismatch.AddScoped<IOwnershipSource, UnavailableOwnershipSource>();
        check(!ProductionRuntimeCompositionInspector.Inspect(catalogMismatch).Ownership,
            "17V unreviewed fallback source fails exact ownership catalog");
        var wrongLifetime = Copy();
        wrongLifetime.Remove(wrongLifetime.Single(x => x.ServiceType == typeof(IOwnershipSource)));
        wrongLifetime.AddSingleton<IOwnershipSource, OwnershipPersistenceSource>();
        check(!ProductionRuntimeCompositionInspector.Inspect(wrongLifetime).Ownership,
            "17V wrong ownership-source lifetime denies readiness");
        var duplicate = Copy();
        duplicate.AddScoped<IOwnershipSource, OwnershipPersistenceSource>();
        check(!ProductionRuntimeCompositionInspector.Inspect(duplicate).Ownership,
            "17V duplicate ownership source denies readiness");

        services.AddScoped<IOwnershipSource, UnavailableOwnershipSource>();
        check(!ProductionRuntimeCompositionInspector.Inspect(services).Ownership,
            "17V mixed fallback and duplicate ownership source cannot claim readiness");
        ToolIdentifier.TryCreate("owned-project-metadata-read", out var tool);
        var user = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        var project = Guid.NewGuid();
        var actionId = ActionIdentifier.RestoreTrusted(Guid.NewGuid());
        var exact = new OwnershipLookupRequest(user, conversation, project);
        var source = new ExactActionSource(exact, actionId.Value);
        var validator = new ExactOwnershipScopeValidator(new FixedUser(user), source);
        ActionDescriptor Descriptor(Guid id, Guid u, Guid c, Guid p) =>
            new(ActionIdentifier.RestoreTrusted(id), new ActionScope(u, c, p), tool!, "test",
                ActionLifecycleStatus.Approved, DateTime.UtcNow, DateTime.UtcNow);
        async Task<bool> Allows(Guid id, Guid u, Guid c, Guid p) =>
            await validator.ValidateActionAsync(ExactActionOwnershipScope.FromTrustedAction(
                Descriptor(id, u, c, p)), CancellationToken.None);
        check(await Allows(actionId.Value, user, conversation, project),
            "17V exact owned action and owned user-conversation-project chain allow");
        check(!await Allows(Guid.NewGuid(), user, conversation, project),
            "17V foreign or missing action denies");
        check(!await Allows(actionId.Value, user, Guid.NewGuid(), project),
            "17V action-conversation mismatch denies");
        check(!await Allows(actionId.Value, user, conversation, Guid.NewGuid()),
            "17V action-project mismatch denies");
        check(!await Allows(actionId.Value, Guid.NewGuid(), conversation, project),
            "17V foreign action user chain denies");
        check(!await Allows(Guid.Empty, user, conversation, project) &&
            !await Allows(actionId.Value, Guid.Empty, conversation, project),
            "17V malformed action and user scope deny");
        check(typeof(ExactActionOwnershipScope).GetConstructors().Length == 0 &&
            typeof(ExactActionOwnershipScope).GetProperties().All(p => p.SetMethod is null),
            "17V action ID comes from trusted action descriptor, not a public scope constructor");

        var sourceText = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
            "Aura.Infrastructure", "Data", "OwnershipPersistenceSource.cs"));
        check(sourceText.Contains("db.AIActions.AsNoTracking().AnyAsync") &&
            sourceText.Contains("a.Id == id && a.UserId == request.UserId") &&
            sourceText.Contains("a.ConversationId == request.ConversationId") &&
            sourceText.Contains("a.ProjectId == request.ProjectId") &&
            sourceText.Contains("c.ProjectId == request.ProjectId") &&
            sourceText.Contains("p.UserId == request.UserId"),
            "17V database action query binds exact action-conversation-project-user chain");
        var root = Directory.GetCurrentDirectory();
        foreach (var (path, method) in new[]
        {
            ("Actions/ActionLifecycleService.cs", "TransitionAsync"),
            ("Approvals/ApprovalWorkflowService.cs", "DecideAsync"),
            ("Executions/TrustedToolExecutionService.cs", "ExecuteAsync"),
            ("Verifications/VerificationWorkflowService.cs", "VerifyAsync"),
            ("Executions/ExecutionReconciliationService.cs", "ReconcileAsync")
        })
        {
            var workflow = File.ReadAllText(Path.Combine(root, "Aura.Application",
                path.Replace('/', Path.DirectorySeparatorChar)));
            check(workflow.Contains(method) &&
                workflow.Contains("scopes.ValidateActionAsync(") &&
                workflow.Contains("ExactActionOwnershipScope.FromTrusted"),
                "17V " + method + " requires exact action ownership before sensitive work");
        }
        var liveOutput = new List<string>();
        check(await LiveSignedJwtOwnershipEvidence.RunAsync(_ => null, liveOutput.Add) == 2 &&
            liveOutput.SequenceEqual(["LIVE SIGNED JWT OWNERSHIP: SKIPPED", "RESULT: INCOMPLETE"]),
            "17V signed JWT harness skips before credentials or database access without gates");
        liveOutput.Clear();
        check(await LiveSignedJwtOwnershipEvidence.RunAsync(key => key switch
            {
                "AURA_LIVE_PRODUCTION_OWNERSHIP_EVIDENCE" => "true",
                "AURA_PRODUCTION_EVIDENCE_DB_IS_DISPOSABLE" => "true",
                _ => null
            }, liveOutput.Add) == 2 &&
            liveOutput.SequenceEqual(["INPUT: FAIL", "RESULT: INCOMPLETE"]),
            "17V signed JWT harness requires exact disposable target identity gates");
    }

    private sealed class FixedUser(Guid id) : ICurrentUserContext
    {
        public Task<CurrentUserSnapshot> GetAsync(CancellationToken token) =>
            Task.FromResult(new CurrentUserSnapshot(id, CurrentUserStatus.Authenticated));
    }

    private sealed class ExactActionSource(OwnershipLookupRequest expected, Guid actionId) : IOwnershipSource
    {
        public Guid LastActionId { get; private set; }
        public Task<OwnershipLookupResult> GetAsync(OwnershipLookupRequest request,
            CancellationToken token) => Task.FromResult(request == expected
                ? new OwnershipLookupResult(OwnershipLookupStatus.Found, request, true, true, true)
                : new OwnershipLookupResult(OwnershipLookupStatus.Denied, null, false, false, false));
        public Task<bool> OwnsActionAsync(OwnershipLookupRequest request,
            ActionIdentifier action, CancellationToken token)
        {
            LastActionId = action.Value;
            return Task.FromResult(request == expected && action.Value == actionId);
        }
    }

    private static bool Exact<TContract, TImplementation>(IServiceCollection services) =>
        services.Count(x => x.ServiceType == typeof(TContract) &&
            x.ImplementationType == typeof(TImplementation) &&
            x.Lifetime == ServiceLifetime.Scoped) == 1;
}
