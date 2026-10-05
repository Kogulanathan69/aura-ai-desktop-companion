using Aura.Application.Privacy.Models;

namespace Aura.Application.Privacy.Interfaces;

public interface IPrivacyGuard
{
    PrivacyGuardResult Evaluate(PrivacyGuardInput input);
}
