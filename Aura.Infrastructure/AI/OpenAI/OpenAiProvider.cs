using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aura.Application.AI.DTOs;
using Aura.Application.AI.Models;
using Aura.Application.AI.Providers;
using Aura.Application.AI.Validation;
using Aura.Application.Common.Exceptions;

namespace Aura.Infrastructure.AI.OpenAI;

public sealed class OpenAiProvider(HttpClient client, OpenAiOptions options) : ICloudAiProvider
{
    public async Task<AiGenerationResult> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return new(AiGenerationStatus.Disabled);
        if (!OpenAiConfiguration.TryValidate(options)) return new(AiGenerationStatus.InvalidConfiguration);
        var prompt = AiGenerationValidation.NormalizePrompt(request.Prompt);
        if (prompt.Contains('\0')) throw new AppValidationException("Prompt contains unsupported text.");
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, OpenAiConfiguration.ResponsesUri)
            {
                Content = JsonContent.Create(new ResponsesRequest(options.Model, prompt, false, false))
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return new(AiGenerationStatus.Unavailable);
            if (response.Content.Headers.ContentLength > OpenAiOptions.MaximumResponseBytes)
                return new(AiGenerationStatus.InvalidResponse);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[OpenAiOptions.MaximumResponseBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(total), deadline.Token);
                if (count == 0) break;
                total += count;
            }
            if (total > OpenAiOptions.MaximumResponseBytes) return new(AiGenerationStatus.InvalidResponse);
            string? text;
            try
            {
                _ = new UTF8Encoding(false, true).GetCharCount(buffer, 0, total);
                using var document = JsonDocument.Parse(buffer.AsMemory(0, total));
                text = ExtractText(document.RootElement);
            }
            catch (InvalidOperationException) { return new(AiGenerationStatus.InvalidResponse); }
            deadline.Token.ThrowIfCancellationRequested();
            return text is null ? new(AiGenerationStatus.InvalidResponse) :
                new(AiGenerationStatus.Success, text, "OpenAI", options.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException(cancellationToken); }
        catch (OperationCanceledException) { return new(AiGenerationStatus.TimedOut); }
        catch (JsonException) { return new(AiGenerationStatus.InvalidResponse); }
        catch (EncoderFallbackException) { return new(AiGenerationStatus.InvalidResponse); }
        catch (DecoderFallbackException) { return new(AiGenerationStatus.InvalidResponse); }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        { return new(AiGenerationStatus.Unavailable); }
    }

    private static string? ExtractText(JsonElement root)
    {
        if (!Is(root, "object", "response") || !Is(root, "status", "completed") ||
            !root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Null ||
            !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return null;
        var text = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            // Reasoning metadata is never returned or replayed. All tool/unknown items fail closed.
            if (Is(item, "type", "reasoning")) continue;
            if (!Is(item, "type", "message") || !Is(item, "role", "assistant") || !Is(item, "status", "completed") ||
                !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
            foreach (var part in content.EnumerateArray())
            {
                if (!Is(part, "type", "output_text") || !part.TryGetProperty("text", out var value) ||
                    value.ValueKind != JsonValueKind.String) return null;
                var segment = value.GetString()!;
                if (segment.Contains('\0') || segment.Length > OpenAiOptions.MaximumResponseBytes - text.Length) return null;
                text.Append(segment);
            }
        }
        var result = text.ToString();
        return string.IsNullOrWhiteSpace(result) || new UTF8Encoding(false, true).GetByteCount(result) > OpenAiOptions.MaximumResponseBytes
            ? null : result;
    }

    private static bool Is(JsonElement item, string name, string value) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && property.GetString() == value;

    private sealed record ResponsesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("store")] bool Store,
        [property: JsonPropertyName("stream")] bool Stream);
}
