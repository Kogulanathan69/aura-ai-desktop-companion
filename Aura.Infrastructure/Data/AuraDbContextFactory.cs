using Microsoft.EntityFrameworkCore;

namespace Aura.Infrastructure.Data;

// The same immutable options instance configures scoped and independently created contexts.
public sealed class AuraDbContextFactory(DbContextOptions<AuraDbContext> options)
    : IDbContextFactory<AuraDbContext>
{
    public AuraDbContext CreateDbContext() => new(options);

    public Task<AuraDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}
