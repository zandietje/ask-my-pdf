namespace AskMyPdf.Infrastructure.Ai;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

/// <summary>
/// Generates text embeddings via the OpenAI embeddings API using raw HttpClient.
/// No additional NuGet dependency needed — just standard System.Net.Http.
/// When no API key is configured, all methods return null (graceful degradation to FTS5-only).
/// </summary>
public class EmbeddingService(HttpClient httpClient, EmbeddingOptions options, ILogger<EmbeddingService> logger)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.ApiKey);

    public int Dimensions => options.Dimensions;

    /// <summary>
    /// Generates embeddings for a batch of texts. Returns null if the API is not configured or fails.
    /// </summary>
    public async Task<float[][]?> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return null;

        if (texts.Count == 0)
            return [];

        try
        {
            var request = new EmbeddingRequest(options.Model, texts.ToList(), options.Dimensions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
            {
                Content = JsonContent.Create(request, options: JsonOptions),
            };
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);

            using var response = await httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("OpenAI embeddings API returned {Status}: {Body}",
                    response.StatusCode, errorBody[..Math.Min(errorBody.Length, 200)]);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, ct);
            if (result?.Data is null || result.Data.Count != texts.Count)
            {
                logger.LogWarning("OpenAI embeddings response had unexpected shape");
                return null;
            }

            return result.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate embeddings");
            return null;
        }
    }

    /// <summary>Generates a single embedding. Returns null on failure.</summary>
    public async Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var result = await GenerateEmbeddingsAsync([text], ct);
        return result?[0];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Request/response DTOs for OpenAI embeddings API
    private record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] List<string> Input,
        [property: JsonPropertyName("dimensions")] int? Dimensions);

    private record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingData> Data);

    private record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}

public record EmbeddingOptions(
    string? ApiKey = null,
    string Model = "text-embedding-3-small",
    int Dimensions = 1536);
