using System.Net;
using System.Text;
using System.Text.Json;
using Aura.Application.AI.DTOs;
using Aura.Application.AI.Models;
using Aura.Application.AI.Validation;
using Aura.Application.Common.Exceptions;
using Aura.Infrastructure.AI.Ollama;

internal static class OllamaChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var valid = new OllamaOptions { Enabled = true, Model = "local-test:latest" };
        var calls = 0;
        using var handler = new FakeOllamaHandler(async (request, token) =>
        {
            calls++;
            check(request.RequestUri?.Host == "127.0.0.1" && request.RequestUri.AbsolutePath == "/api/generate" &&
                request.Method == HttpMethod.Post, "localhost is pinned to numeric loopback generate path");
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            check(body.RootElement.GetProperty("prompt").GetString() == "Hello" &&
                body.RootElement.GetProperty("model").GetString() == valid.Model &&
                !body.RootElement.GetProperty("stream").GetBoolean(), "trimmed bounded prompt uses non-streaming Ollama transport");
            return Json("{\"response\":\"Untrusted output\",\"done\":true,\"model\":\"ignored transport field\"}");
        });
        using var client = new HttpClient(handler);
        var disabled = new OllamaAiProvider(client, new());
        check((await disabled.GenerateAsync(new(null!))).Status == AiGenerationStatus.Disabled && calls == 0,
            "disabled default performs no validation/HTTP even for null prompt");
        var provider = new OllamaAiProvider(client, valid);
        foreach (var prompt in new[] { " \t ", new string('x', AiGenerationValidation.MaximumPromptCharacters + 1) })
        {
            var rejected = false;
            try { await provider.GenerateAsync(new(prompt)); } catch (AppValidationException) { rejected = true; }
            check(rejected && calls == 0, "invalid/oversized prompt rejected before HTTP");
        }
        foreach (var host in new[] { "http://localhost:11434", "http://127.0.0.1:11434/", "http://[::1]:11434" })
            check(OllamaConfiguration.TryValidate(new() { Enabled = true, Model = "local-test", BaseUrl = host }, out var destination) &&
                IPAddress.IsLoopback(IPAddress.Parse(destination!.Host.Trim('[', ']'))), "explicit loopback accepted: " + host);
        foreach (var host in new[] { "https://localhost:11434", "ftp://localhost", "http://example.com", "http://8.8.8.8",
            "http://192.168.1.2", "http://user@localhost:11434", "not a URL", "http://localhost.evil.test", "http://localhost.",
            "http://2130706433", "http://127.1", "http://0x7f000001", "http://[::ffff:127.0.0.1]", "http://localhost/api",
            "http://localhost?next=remote", "http://localhost#fragment", "http://localhost:0", "http://localhost:65536", " http://localhost" })
        {
            var invalid = new OllamaAiProvider(client, new() { Enabled = true, Model = "local-test", BaseUrl = host });
            check((await invalid.GenerateAsync(new("Hello"))).Status == AiGenerationStatus.InvalidConfiguration && calls == 0,
                "unsafe/ambiguous destination rejected without HTTP: " + host);
        }
        foreach (var config in new[] { new OllamaOptions { Enabled = true },
            new OllamaOptions { Enabled = true, Model = "local-test", TimeoutSeconds = 0 },
            new OllamaOptions { Enabled = true, Model = "local-test", TimeoutSeconds = 121 },
            new OllamaOptions { Enabled = true, Model = new string('m', 129) },
            new OllamaOptions { Enabled = true, Model = "model:cloud" },
            new OllamaOptions { Enabled = true, Model = "model-cloud" },
            new OllamaOptions { Enabled = true, Model = " local-test " } })
            check((await new OllamaAiProvider(client, config).GenerateAsync(new("Hello"))).Status ==
                AiGenerationStatus.InvalidConfiguration && calls == 0, "invalid model/timeout configuration fails closed");
        var generated = await provider.GenerateAsync(new(" Hello "));
        check(generated.Status == AiGenerationStatus.Success && generated.Content == "Untrusted output" &&
            generated.Provider == "Ollama" && generated.Model == valid.Model && calls == 1,
            "safe provider-neutral result excludes extra/raw transport fields");
        using var realHandler = OllamaConfiguration.CreateHandler();
        check(!realHandler.AllowAutoRedirect && !realHandler.UseProxy && !realHandler.UseCookies &&
            !realHandler.UseDefaultCredentials && realHandler.Credentials is null && !realHandler.PreAuthenticate &&
            realHandler.AutomaticDecompression == DecompressionMethods.None && realHandler.ServerCertificateCustomValidationCallback is null,
            "production handler forbids redirect/proxy/cookies/credentials and preserves certificate policy");

        async Task<AiGenerationResult> Respond(HttpResponseMessage response)
        {
            using var stub = new FakeOllamaHandler((_, _) => Task.FromResult(response));
            using var stubClient = new HttpClient(stub);
            var result = await new OllamaAiProvider(stubClient, valid).GenerateAsync(new("Hello"));
            check(stub.Calls == 1, "single HTTP request with no retries or cloud fallback");
            return result;
        }
        var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        redirect.Headers.Location = new Uri("https://remote.invalid");
        check((await Respond(redirect)).Status == AiGenerationStatus.Unavailable, "redirect status rejected; remote Location not followed");
        check((await Respond(new(HttpStatusCode.InternalServerError))).Status == AiGenerationStatus.Unavailable,
            "non-success HTTP response fails safely");
        foreach (var body in new[] { "not-json", "{}", "null", "{\"done\":true}",
            "{\"response\":\"x\",\"done\":false}", "{\"response\":\" \",\"done\":true}",
            "{\"response\":\"x\",\"done\":true,\"error\":\"private detail\"}" })
        {
            var failure = await Respond(Json(body));
            check(failure.Status == AiGenerationStatus.InvalidResponse && failure.Content is null && failure.Model is null,
                "malformed/incomplete/error response returns no provider details");
        }
        var large = Json(new string('x', OllamaOptions.MaximumResponseBytes + 1));
        check((await Respond(large)).Status == AiGenerationStatus.InvalidResponse, "oversized declared response rejected");
        var streamed = new CountingResponseStream(OllamaOptions.MaximumResponseBytes + 100);
        check((await Respond(new(HttpStatusCode.OK) { Content = new StreamContent(streamed) })).Status == AiGenerationStatus.InvalidResponse &&
            streamed.BytesRead == OllamaOptions.MaximumResponseBytes + 1, "unknown-length growing response reads at most limit plus detection byte");
        var exactBody = "{\"response\":\"" + new string('x', OllamaOptions.MaximumResponseBytes - 27) + "\",\"done\":true}";
        check(Encoding.UTF8.GetByteCount(exactBody) == OllamaOptions.MaximumResponseBytes &&
            (await Respond(Json(exactBody))).Status == AiGenerationStatus.Success, "exact response byte limit accepted");
        using var unavailable = new FakeOllamaHandler((_, _) => throw new HttpRequestException("sensitive network detail"));
        using var unavailableClient = new HttpClient(unavailable);
        check((await new OllamaAiProvider(unavailableClient, valid).GenerateAsync(new("Hello"))).Status == AiGenerationStatus.Unavailable &&
            unavailable.Calls == 1, "network exception sanitized with no fallback");
        using var slow = new FakeOllamaHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json("{}"); });
        using var slowClient = new HttpClient(slow);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledObserved = false;
        try { await new OllamaAiProvider(slowClient, valid).GenerateAsync(new("Hello"), cancelled.Token); }
        catch (OperationCanceledException) { cancelledObserved = true; }
        check(cancelledObserved && slow.Calls == 0, "pre-cancelled caller propagates without HTTP");
        using var duringSend = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        cancelledObserved = false;
        try { await new OllamaAiProvider(slowClient, valid).GenerateAsync(new("Hello"), duringSend.Token); }
        catch (OperationCanceledException) { cancelledObserved = true; }
        check(cancelledObserved, "caller cancellation propagates during HTTP");
        var timeoutOptions = new OllamaOptions { Enabled = true, Model = "local-test", TimeoutSeconds = 1 };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        check((await new OllamaAiProvider(slowClient, timeoutOptions).GenerateAsync(new("Hello"))).Status == AiGenerationStatus.TimedOut &&
            watch.Elapsed < TimeSpan.FromSeconds(5), "configured one-second deadline bounds stalled headers");
        using var bodyHandler = new FakeOllamaHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new CountingResponseStream(100, stall: true)) }));
        using var bodyClient = new HttpClient(bodyHandler);
        check((await new OllamaAiProvider(bodyClient, timeoutOptions).GenerateAsync(new("Hello"))).Status == AiGenerationStatus.TimedOut,
            "deadline also bounds stalled body after headers");
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
internal sealed class FakeOllamaHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Calls++; return send(request, cancellationToken); }
}
internal sealed class CountingResponseStream(int length, bool stall = false) : Stream
{
    public int BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (stall) await Task.Delay(Timeout.Infinite, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var count = Math.Min(buffer.Length, length - BytesRead);
        buffer.Span[..count].Fill((byte)'x');
        BytesRead += count;
        return count;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
