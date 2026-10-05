namespace Aura.Application.Common.Interfaces;

public interface ICurrentUserService
{
    string? AuthUserId { get; }

    bool IsAuthenticated { get; }
}