using System.Net;

namespace Aura.Infrastructure.AI.OpenAI;

public static class OpenAiConfiguration
{
    public static Uri ResponsesUri { get; } = new("https://api.openai.com/v1/responses");

    public static bool TryValidate(OpenAiOptions options) =>
        options.TimeoutSeconds is >= 1 and <= 120 &&
        options.ApiKey is { Length: > 0 and <= 512 } &&
        !string.IsNullOrWhiteSpace(options.ApiKey) && !options.ApiKey.Any(char.IsControl) &&
        options.Model is { Length: > 0 and <= 100 } && char.IsAsciiLetterOrDigit(options.Model[0]) &&
        options.Model.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/');

    public static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        UseDefaultCredentials = false, Credentials = null, PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.None
    };
}
