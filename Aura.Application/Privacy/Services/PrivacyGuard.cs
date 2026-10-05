using System.Text.RegularExpressions;
using Aura.Application.Privacy.Interfaces;
using Aura.Application.Privacy.Models;
using Aura.Application.Privacy.Rules;

namespace Aura.Application.Privacy.Services;

public sealed class PrivacyGuard : IPrivacyGuard
{
    public PrivacyGuardResult Evaluate(PrivacyGuardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (PrivacyGuardRules.IsBlockedMetadata(input.RelativePath, input.FileName, input.Extension))
            return new(PrivacyDecision.Block, ["BLOCKED_SENSITIVE_FILE_TYPE"]);

        if (string.IsNullOrEmpty(input.Content))
            return new(PrivacyDecision.Allow, []);

        try
        {
            if (PrivacyGuardRules.PrivateKeyBlock.IsMatch(input.Content))
                return new(PrivacyDecision.Block, ["BLOCKED_PRIVATE_KEY_CONTENT"]);

            var reasons = new List<string>();
            var redacted = PrivacyGuardRules.AuthorizationBearer.Replace(input.Content, match =>
                match.Groups["prefix"].Value + "[REDACTED]");
            if (redacted != input.Content)
                reasons.Add("REDACTED_BEARER_TOKEN");

            var sanitized = PrivacyGuardRules.CredentialAssignment.Replace(redacted, match =>
                match.Groups["prefix"].Value + "[REDACTED]");
            if (sanitized != redacted)
                reasons.Add("REDACTED_CREDENTIAL_ASSIGNMENT");

            return reasons.Count == 0
                ? new(PrivacyDecision.Allow, [])
                : new(PrivacyDecision.Redact, reasons, sanitized);
        }
        catch (RegexMatchTimeoutException)
        {
            return new(PrivacyDecision.Block, ["BLOCKED_RULE_EVALUATION_TIMEOUT"]);
        }
    }
}
