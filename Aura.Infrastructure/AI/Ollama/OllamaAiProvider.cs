using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aura.Application.AI.DTOs;
using Aura.Application.AI.Interfaces;
using Aura.Application.AI.Models;
using Aura.Application.AI.Validation;

namespace Aura.Infrastructure.AI.Ollama;

public sealed class OllamaAiProvider(HttpClient client, OllamaOptions options) : ILocalAiProvider
{
    public async Task<AiGenerationResult> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return new(AiGenerationStatus.Disabled);
        if (!OllamaConfiguration.TryValidate(options, out var uri)) return new(AiGenerationStatus.InvalidConfiguration);
        var prompt = AiGenerationValidation.NormalizePrompt(request.Prompt);
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(new GenerateTransportRequest(options.Model, prompt, false))
            };
            // Bound the entire operation, including response streaming after headers.
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return new(AiGenerationStatus.Unavailable);
            if (response.Content.Headers.ContentLength > OllamaOptions.MaximumResponseBytes)
                return new(AiGenerationStatus.InvalidResponse);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[OllamaOptions.MaximumResponseBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), deadline.Token);
                if (count == 0) break;
                total += count;
            }
            if (total > OllamaOptions.MaximumResponseBytes) return new(AiGenerationStatus.InvalidResponse);
            var result = JsonSerializer.Deserialize<GenerateTransportResponse>(buffer.AsSpan(0, total));
            deadline.Token.ThrowIfCancellationRequested();
            if (result is null || !result.Done || string.IsNullOrWhiteSpace(result.Response) || result.Error is not null)
                return new(AiGenerationStatus.InvalidResponse);
            return new(AiGenerationStatus.Success, result.Response, "Ollama", options.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) { return new(AiGenerationStatus.TimedOut); }
        catch (JsonException) { return new(AiGenerationStatus.InvalidResponse); }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return new(AiGenerationStatus.Unavailable);
        }
    }

    private sealed record GenerateTransportRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);
    private sealed record GenerateTransportResponse(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("error")] string? Error);
}
