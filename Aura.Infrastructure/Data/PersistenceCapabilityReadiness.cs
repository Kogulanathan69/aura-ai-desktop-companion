using Aura.Application.Security;

namespace Aura.Infrastructure.Data;

// Informational only; deliberately not registered at startup. A database has not
// been probed and the authenticated current-user boundary remains unavailable.
public sealed class PersistenceCapabilityReadiness : IRuntimeCapabilityReadiness
{
    public RuntimeCapabilitySnapshot GetSnapshot() => new(true, false, false, false, false,
        false, false, false, false);
}
