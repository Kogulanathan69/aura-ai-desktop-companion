using Aura.Application.Approvals;
using Aura.Application.Executions;
using Aura.Application.Verifications;

namespace Aura.Application.Actions;

// A future host adapter must resolve this from the authenticated request, never from ActionScope.
public enum CurrentUserStatus { Unavailable, Authenticated }
public sealed record CurrentUserSnapshot(Guid UserId, CurrentUserStatus Status);

public interface ICurrentUserContext
{
    Task<CurrentUserSnapshot> GetAsync(CancellationToken cancellationToken);
}

public sealed class UnavailableCurrentUserContext : ICurrentUserContext
{
    public Task<CurrentUserSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CurrentUserSnapshot(Guid.Empty, CurrentUserStatus.Unavailable));
    }
}

// A source must assess current owned conversation, exact conversation-to-project link,
// and owned project for this one user/scope. IDs supplied by the caller prove nothing.
public sealed record OwnershipLookupRequest(Guid UserId, Guid ConversationId, Guid ProjectId);
// Constructed from an application-owned action descriptor, never a request-body ID.
public sealed record ExactActionOwnershipScope
{
    public ActionScope Scope { get; }
    public ActionIdentifier ActionId { get; }
    private ExactActionOwnershipScope(ActionScope scope, ActionIdentifier actionId)
    {
        Scope = scope;
        ActionId = actionId;
    }
    public static ExactActionOwnershipScope FromTrustedAction(ActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new(action.Scope, action.Id);
    }
    // A reconciliation snapshot is returned by the persistence source and checked
    // against its stored state before this factory is called.
    internal static ExactActionOwnershipScope FromTrustedPersistedAction(ActionScope scope,
        ActionIdentifier actionId) => new(scope, actionId);
}
public enum OwnershipLookupStatus { Unavailable, Denied, Found }
public sealed record OwnershipLookupResult(OwnershipLookupStatus Status, OwnershipLookupRequest? Binding,
    bool OwnsConversation, bool HasExactProjectRelation, bool OwnsProject);

public interface IOwnershipSource
{
    Task<OwnershipLookupResult> GetAsync(OwnershipLookupRequest request,
        CancellationToken cancellationToken);
    // Existing sources deny until they explicitly implement the exact action query.
    Task<bool> OwnsActionAsync(OwnershipLookupRequest request, ActionIdentifier actionId,
        CancellationToken cancellationToken) => Task.FromResult(false);
}

public interface IExactActionOwnershipValidator
{
    Task<bool> ValidateActionAsync(ExactActionOwnershipScope actionScope,
        CancellationToken cancellationToken);
}

public sealed class UnavailableOwnershipSource : IOwnershipSource
{
    public Task<OwnershipLookupResult> GetAsync(OwnershipLookupRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OwnershipLookupResult(OwnershipLookupStatus.Unavailable,
            null, false, false, false));
    }
}

// One ownership decision is shared by all five workflow entry points. It does not
// establish permission, approval, execution eligibility, verification, or audit success.
public sealed class ExactOwnershipScopeValidator(ICurrentUserContext currentUser, IOwnershipSource source) :
    IExactActionOwnershipValidator, IActionScopeValidator, IActionApprovalScopeValidator, IToolExecutionScopeValidator,
    IVerificationScopeValidator, IExecutionReconciliationScopeValidator
{
    public async Task<bool> ValidateActionAsync(ExactActionOwnershipScope actionScope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (actionScope?.Scope is not { } scope || actionScope.ActionId?.Value is not { } actionId ||
            actionId == Guid.Empty || !await ValidateAsync(scope, cancellationToken))
            return false;
        try
        {
            // Re-read authenticated identity immediately before the exact action query.
            var user = await currentUser.GetAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (user.Status != CurrentUserStatus.Authenticated || user.UserId != scope.UserId)
                return false;
            return await source.OwnsActionAsync(new(user.UserId, scope.ConversationId,
                scope.ProjectId), actionScope.ActionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { cancellationToken.ThrowIfCancellationRequested(); return false; }
    }

    public async Task<bool> ValidateAsync(ActionScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope is null || scope.UserId == Guid.Empty || scope.ConversationId == Guid.Empty ||
            scope.ProjectId == Guid.Empty)
            return false;

        try
        {
            var user = await currentUser.GetAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (user is null || user.Status != CurrentUserStatus.Authenticated ||
                user.UserId == Guid.Empty || user.UserId != scope.UserId)
                return false;

            var request = new OwnershipLookupRequest(user.UserId, scope.ConversationId, scope.ProjectId);
            var result = await source.GetAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result is not null && result.Status == OwnershipLookupStatus.Found &&
                result.Binding == request && result.OwnsConversation &&
                result.HasExactProjectRelation && result.OwnsProject;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }
}
