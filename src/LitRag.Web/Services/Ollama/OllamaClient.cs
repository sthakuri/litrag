using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace LitRag.Web.Services.Ollama;

public record OllamaChatMessage(string Role, string Content);

public class OllamaException(string message) : Exception(message);

/// <summary>
/// Retrieval-tuned embedding models (e.g. nomic-embed-text) expect a task prefix on the input text
/// to get good similarity results — one prefix for stored passages, a different one for search queries.
/// Mixing these up (or omitting them) noticeably degrades retrieval quality.
/// </summary>
public enum EmbeddingKind
{
    Document,
    Query
}

public class OllamaClient(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaClient> logger)
{
    private readonly OllamaOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await httpClient.GetAsync("/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Ollama is not reachable at {BaseUrl}", httpClient.BaseAddress);
            return false;
        }
    }

    public async Task<float[]> EmbedAsync(string text, EmbeddingKind kind, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        // nomic-embed-text (the documented default) is trained with these task prefixes and retrieves
        // noticeably worse without them; other embedding models generally ignore an unfamiliar prefix,
        // but to be safe this only applies to models that look like a nomic embedding model.
        var promptText = _options.EmbeddingModel.Contains("nomic", StringComparison.OrdinalIgnoreCase)
            ? (kind == EmbeddingKind.Query ? "search_query: " : "search_document: ") + text
            : text;

        var request = new EmbeddingRequest(_options.EmbeddingModel, promptText);
        using var response = await httpClient.PostAsJsonAsync("/api/embeddings", request, JsonOptions, cts.Token);
        await EnsureSuccessAsync(response, cts.Token);

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, cts.Token);
        return result?.Embedding ?? throw new InvalidOperationException("Ollama returned no embedding.");
    }

    /// <summary>Non-streaming generate call constrained to JSON output, used for paper validation/classification.</summary>
    public Task<string> GenerateJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        GenerateAsync(systemPrompt, userPrompt, format: "json", ct);

    /// <summary>Non-streaming generate call for free-form prose, used e.g. for paper summaries.</summary>
    public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        GenerateAsync(systemPrompt, userPrompt, format: null, ct);

    private async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string? format, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        var request = new GenerateRequest(_options.ChatModel, userPrompt, systemPrompt, format, false);
        using var response = await httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions, cts.Token);
        await EnsureSuccessAsync(response, cts.Token);

        var result = await response.Content.ReadFromJsonAsync<GenerateResponse>(JsonOptions, cts.Token);
        return result?.Response ?? string.Empty;
    }

    /// <summary>Streams assistant response text deltas for a chat completion.</summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<OllamaChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new ChatRequest(_options.ChatModel, messages, true);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ChatStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatStreamChunk>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipping malformed Ollama stream chunk: {Line}", line);
                continue;
            }

            if (chunk?.Message?.Content is { Length: > 0 } content)
            {
                yield return content;
            }

            if (chunk?.Done == true)
            {
                yield break;
            }
        }
    }

    /// <summary>Surfaces Ollama's JSON error body (e.g. "model 'x' not found, try pulling it first") instead of a bare status code.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception)
        {
            // best-effort; fall through with a generic message below
        }

        string? detail = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                detail = JsonSerializer.Deserialize<OllamaErrorResponse>(body, JsonOptions)?.Error;
            }
            catch (JsonException)
            {
                detail = body;
            }
        }

        throw new OllamaException($"Ollama returned {(int)response.StatusCode} {response.ReasonPhrase}: {detail ?? "no further details"}");
    }

    private record OllamaErrorResponse([property: JsonPropertyName("error")] string? Error);

    private record EmbeddingRequest(string Model, string Prompt);
    private record EmbeddingResponse(float[] Embedding);

    private record GenerateRequest(string Model, string Prompt, string System, string? Format, bool Stream);
    private record GenerateResponse(string Response);

    private record ChatRequest(string Model, IReadOnlyList<OllamaChatMessage> Messages, bool Stream);

    private record ChatStreamChunk(
        [property: JsonPropertyName("message")] ChatStreamMessage? Message,
        [property: JsonPropertyName("done")] bool Done);

    private record ChatStreamMessage([property: JsonPropertyName("content")] string? Content);
}
