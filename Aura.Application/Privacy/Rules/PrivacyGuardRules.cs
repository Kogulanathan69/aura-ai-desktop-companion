using System.Text.RegularExpressions;

namespace Aura.Application.Privacy.Rules;

internal static class PrivacyGuardRules
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    internal static readonly Regex PrivateKeyBlock = new(
        @"-----BEGIN (?:[A-Z0-9 ]+ )?PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    internal static readonly Regex CredentialAssignment = new(
        @"(?<prefix>\b(?:password|pwd|api[_-]?key|access[_-]?token|bearer[_-]?token)\b\s*[:=]\s*)(?<value>""[^""\r\n]*""|'[^'\r\n]*'|[^\s;,\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    internal static readonly Regex AuthorizationBearer = new(
        @"(?<prefix>\bAuthorization\s*:\s*Bearer\s+)(?<value>[^\s,;\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    internal static bool IsBlockedMetadata(string? relativePath, string? fileName, string? extension)
    {
        var pathName = relativePath?.Replace('\\', '/').Split('/').LastOrDefault();
        return IsBlockedName(pathName) || IsBlockedName(fileName) ||
            extension?.Trim().ToLowerInvariant() is ".pfx" or ".p12" or ".key";
    }

    private static bool IsBlockedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var normalized = name.Trim().ToLowerInvariant();
        return normalized is ".env" or "id_rsa" or "id_dsa" or "id_ecdsa" or "id_ed25519"
            or "credentials.json" or "secrets.json" or "private.pem" or "private_key.pem" or "private-key.pem"
            || normalized.StartsWith(".env.", StringComparison.Ordinal)
            || normalized.EndsWith(".pfx", StringComparison.Ordinal)
            || normalized.EndsWith(".p12", StringComparison.Ordinal)
            || normalized.EndsWith(".key", StringComparison.Ordinal);
    }
}
