using System.Net;
using System.Text;
using System.Text.Json;
using Aura.Application.AI.Chat.Services;
using Aura.Application.AI.Models;
using Aura.Application.AI.Providers;
using Aura.Application.Common.Exceptions;
using Aura.Infrastructure.AI.OpenAI;

internal static class OpenAiChecks
{
    private const string FakeKey = "fixture-not-a-real-key";
    private static OpenAiOptions Options(string key = FakeKey, string model = "configured-model", int timeout = 60) =>
        new() { Enabled = true, ApiKey = key, Model = model, TimeoutSeconds = timeout };
    private static string Body(string text = "Answer") => JsonSerializer.Serialize(new
    {
        @object = "response", status = "completed", error = (string?)null,
        output = new[] { new { type = "message", role = "assistant", status = "completed",
            content = new[] { new { type = "output_text", text } } } }
    });
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static async Task RunAsync(Action<bool, string> check)
    {
        using var handler = new FakeOllamaHandler(async (request, token) =>
        {
            check(request.Method == HttpMethod.Post && request.RequestUri == OpenAiConfiguration.ResponsesUri &&
                request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/responses", "OpenAI fixed HTTPS POST destination");
            check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == FakeKey &&
                request.RequestUri is not null && !request.RequestUri.Query.Contains(FakeKey), "OpenAI secret confined to bearer header");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = body.RootElement;
            check(root.GetProperty("input").GetString() == "Current prompt" &&
                root.GetProperty("model").GetString() == "configured-model", "OpenAI normalized current prompt and server model only");
            check(root.EnumerateObject().Select(x => x.Name).Order().SequenceEqual(new[] { "input", "model", "store", "stream" }),
                "OpenAI exact payload excludes history/project/memory/files/tools/previous_response_id/conversation");
            check(!root.GetProperty("store").GetBoolean() && !root.GetProperty("stream").GetBoolean(),
                "OpenAI non-storage non-streaming request");
            return Json(Body());
        });
        using var client = new HttpClient(handler);
        check((await new OpenAiProvider(client, new()).GenerateAsync(new(null!))).Status == AiGenerationStatus.Disabled && handler.Calls == 0,
            "OpenAI disabled needs no key and performs no HTTP or prompt validation");
        foreach (var invalid in new[] { Options(null!), Options(""), Options("   "), Options("bad\r\nkey"),
            Options("bad\rkey"), Options("bad\nkey"), Options("bad\tkey"), Options("bad\0key"),
            Options("bad\u007fkey"), Options("bad\u0085key"), Options(new string('k', 513)),
            Options(model: ""), Options(model: "bad model"), Options(model: new string('m', 101)),
            Options(timeout: 0), Options(timeout: 121) })
            check((await new OpenAiProvider(client, invalid).GenerateAsync(new("prompt"))).Status == AiGenerationStatus.InvalidConfiguration &&
                handler.Calls == 0, "OpenAI invalid enabled configuration fails before HTTP");
        check(OpenAiConfiguration.TryValidate(Options(timeout: 1)) && OpenAiConfiguration.TryValidate(Options(timeout: 120)),
            "OpenAI timeout endpoints accepted");
        const string opaqueKey = "opaque.fixture+secret/=:@!";
        using var opaqueHandler = new FakeOllamaHandler((request, _) =>
        {
            check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == opaqueKey,
                "OpenAI safe opaque secret preserved in bearer header");
            return Task.FromResult(Json(Body()));
        });
        using var opaqueClient = new HttpClient(opaqueHandler);
        check((await new OpenAiProvider(opaqueClient, Options(opaqueKey)).GenerateAsync(new("prompt"))).Status == AiGenerationStatus.Success &&
            opaqueHandler.Calls == 1, "OpenAI printable punctuation key accepted without assumed key format");
        check(OpenAiConfiguration.TryValidate(Options(new string('k', 512))), "OpenAI exact key length limit accepted");
        using var hardened = OpenAiConfiguration.CreateHandler();
        check(!hardened.AllowAutoRedirect, "OpenAI redirects disabled");
        check(!hardened.UseCookies, "OpenAI cookies disabled");
        check(!hardened.UseDefaultCredentials && hardened.Credentials is null && !hardened.PreAuthenticate, "OpenAI credentials disabled");
        check(!hardened.UseProxy && hardened.Proxy is null, "OpenAI proxy disabled without custom routing");
        check(hardened.ServerCertificateCustomValidationCallback is null && hardened.AutomaticDecompression == DecompressionMethods.None,
            "OpenAI TLS validation intact and decompression disabled");
        check(!typeof(OpenAiOptions).GetProperties().Any(x => x.Name.Contains("Url", StringComparison.OrdinalIgnoreCase)), "OpenAI no configurable URL");
        var provider = new OpenAiProvider(client, Options());
        foreach (var prompt in new[] { " ", new string('x', 16_385), "bad\0prompt" })
        {
            var rejected = false;
            try { await provider.GenerateAsync(new(prompt)); } catch (AppValidationException) { rejected = true; }
            check(rejected && handler.Calls == 0, "OpenAI invalid prompt rejected before HTTP");
        }
        var success = await provider.GenerateAsync(new(" Current prompt "));
        check(success == new AiGenerationResult(AiGenerationStatus.Success, "Answer", "OpenAI", "configured-model") && handler.Calls == 1,
            "OpenAI expected assistant text parsed with safe configured metadata, one call");

        async Task<AiGenerationResult> Respond(HttpResponseMessage response, string prompt = "Private prompt")
        {
            using var fake = new FakeOllamaHandler((_, _) => Task.FromResult(response));
            using var http = new HttpClient(fake);
            var result = await new OpenAiProvider(http, Options()).GenerateAsync(new(prompt));
            check(fake.Calls == 1, "OpenAI response path never retries");
            return result;
        }
        check((await Respond(Json(Body()), new string('x', 16_384))).Status == AiGenerationStatus.Success, "OpenAI exact prompt limit accepted");
        foreach (var body in new[] { "broken", "null", "[]", "{}", Body(""), Body(" "), Body("bad\0text"),
            Body().Replace("completed", "incomplete"), Body().Replace("assistant", "user"),
            Body().Replace("output_text", "refusal"), Body().Replace("message", "function_call"),
            Body().Replace("\"text\":\"Answer\"", "\"text\":123"),
            Body().Replace("Answer", "\\uD800"),
            Body().Replace("\"error\":null", "\"error\":{\"message\":\"Private prompt fixture-not-a-real-key\"}") })
            check((await Respond(Json(body))).Status == AiGenerationStatus.InvalidResponse, "OpenAI malformed/incomplete/empty/NUL/tool output rejected");
        var multi = "{\"object\":\"response\",\"status\":\"completed\",\"error\":null,\"output\":[{\"type\":\"reasoning\"},{\"type\":\"message\",\"role\":\"assistant\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"One\"},{\"type\":\"output_text\",\"text\":\"Two\"}]}]}";
        check((await Respond(Json(multi))).Content == "OneTwo", "OpenAI combines text parts without returning reasoning metadata");
        var failure = await Respond(new(HttpStatusCode.Unauthorized) { Content = new StringContent(FakeKey + "Private prompt") });
        check(failure == new AiGenerationResult(AiGenerationStatus.Unavailable) &&
            !JsonSerializer.Serialize(failure).Contains(FakeKey) && !JsonSerializer.Serialize(failure).Contains("Private prompt"),
            "OpenAI HTTP failure sanitized, secret and prompt absent");
        check((await Respond(new(HttpStatusCode.Redirect) { Headers = { Location = new("https://example.invalid") } })).Status ==
            AiGenerationStatus.Unavailable, "OpenAI redirect response treated as failure without retry");
        var length = Json("{}");
        length.Content.Headers.ContentLength = OpenAiOptions.MaximumResponseBytes + 1;
        check((await Respond(length)).Status == AiGenerationStatus.InvalidResponse, "OpenAI oversized declared response rejected");
        var stream = new CountingResponseStream(OpenAiOptions.MaximumResponseBytes + 100);
        check((await Respond(new(HttpStatusCode.OK) { Content = new StreamContent(stream) })).Status == AiGenerationStatus.InvalidResponse &&
            stream.BytesRead == OpenAiOptions.MaximumResponseBytes + 1, "OpenAI growing stream bounded to detection byte");
        var overhead = Encoding.UTF8.GetByteCount(Body(""));
        check((await Respond(new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 0xff, 0xfe }) })).Status ==
            AiGenerationStatus.InvalidResponse, "OpenAI invalid UTF-8 response fails safely");
        check((await Respond(Json(Body(new string('x', OpenAiOptions.MaximumResponseBytes - overhead))))).Status == AiGenerationStatus.Success,
            "OpenAI exact response body limit accepted");
        foreach (var exception in new Exception[] { new HttpRequestException(FakeKey), new IOException("Private prompt") })
        {
            using var fake = new FakeOllamaHandler((_, _) => throw exception);
            using var http = new HttpClient(fake);
            check((await new OpenAiProvider(http, Options()).GenerateAsync(new("Private prompt"))) == new AiGenerationResult(AiGenerationStatus.Unavailable) &&
                fake.Calls == 1, "OpenAI network/IO failure sanitized with no retry/fallback");
        }
        using var slow = new FakeOllamaHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json("{}"); });
        using var slowClient = new HttpClient(slow);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var observed = false;
        try { await new OpenAiProvider(slowClient, Options()).GenerateAsync(new("prompt"), cancelled.Token); }
        catch (OperationCanceledException) { observed = true; }
        check(observed && slow.Calls == 0, "OpenAI precancellation propagates before HTTP");
        using var during = new CancellationTokenSource(50);
        observed = false;
        try { await new OpenAiProvider(slowClient, Options()).GenerateAsync(new("prompt"), during.Token); }
        catch (OperationCanceledException) { observed = true; }
        check(observed, "OpenAI caller cancellation propagates during HTTP");
        check((await new OpenAiProvider(slowClient, Options(timeout: 1)).GenerateAsync(new("prompt"))).Status == AiGenerationStatus.TimedOut,
            "OpenAI configured deadline bounds stalled headers");
        using var stalledBody = new FakeOllamaHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new CountingResponseStream(100, stall: true)) }));
        using var bodyClient = new HttpClient(stalledBody);
        check((await new OpenAiProvider(bodyClient, Options(timeout: 1)).GenerateAsync(new("prompt"))).Status == AiGenerationStatus.TimedOut,
            "OpenAI configured deadline bounds stalled body");
        check(typeof(AiChatService).GetConstructors().SelectMany(x => x.GetParameters()).All(x => x.ParameterType != typeof(ICloudAiProvider)),
            "Step 11J has no direct cloud transport dependency");
    }
}
