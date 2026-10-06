using System.Net;
using System.Text.RegularExpressions;

namespace Aura.Infrastructure.AI.Ollama;

public static class OllamaConfiguration
{
    private static readonly Regex LoopbackUrl = new(
        @"\Ahttp://(?<host>localhost|127\.0\.0\.1|\[::1\])(?::[0-9]{1,5})?/?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex ModelName = new(@"\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryValidate(OllamaOptions options, out Uri? generationUri)
    {
        generationUri = null;
        if (options.BaseUrl is null || options.BaseUrl.Length > 128 ||
            !LoopbackUrl.IsMatch(options.BaseUrl) || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Port is < 1 or > 65535 || options.TimeoutSeconds is < 1 or > 120 ||
            options.Model is null || options.Model.Length > 128 || !ModelName.IsMatch(options.Model) ||
            options.Model.Contains("cloud", StringComparison.OrdinalIgnoreCase)) return false;
        // No hostname resolution, hosts-file dependence, or arbitrary DNS loopback checks.
        var host = LoopbackUrl.Match(options.BaseUrl).Groups["host"].Value;
        generationUri = new UriBuilder(uri)
        {
            Host = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : uri.Host,
            Path = "/api/generate"
        }.Uri;
        return true;
    }

    public static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.None
    };
}
