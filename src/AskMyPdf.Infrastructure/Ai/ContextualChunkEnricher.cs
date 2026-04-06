namespace AskMyPdf.Infrastructure.Ai;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using AskMyPdf.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implements Anthropic's Contextual Retrieval technique: for each page, calls Haiku
/// to generate short context prefixes for ALL chunks on that page in a single request.
/// Uses prompt caching on the page text for ~90% cost reduction.
/// </summary>
public partial class ContextualChunkEnricher(
    AnthropicClient client,
    ContextualRetrievalOptions options,
    ILogger<ContextualChunkEnricher> logger)
{
    private const int MaxConcurrency = 5;
    private const int MaxRetries = 3;
    private const int TokensPerChunk = 150;

    // Rate limiter: spaces request starts to stay under 50 req/min API limit
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(1300); // ~46 req/min

    /// <summary>
    /// Enriches chunks by prepending LLM-generated context. Batches all chunks from the
    /// same page into a single API call (typically 4-6x fewer calls than per-chunk).
    /// </summary>
    public async Task<List<DocumentChunk>> EnrichChunksAsync(
        List<DocumentChunk> chunks,
        List<PageCanonicalData> pages,
        CancellationToken ct = default)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Contextual retrieval disabled, skipping enrichment");
            return chunks;
        }

        logger.LogInformation("Enriching {Count} chunks with contextual retrieval (model: {Model})",
            chunks.Count, options.Model);

        // Group chunks by page — one API call per page instead of per chunk
        var chunksByPage = chunks.GroupBy(c => c.PageNumber)
            .ToDictionary(g => g.Key, g => g.ToList());

        var pageBlockCache = new Dictionary<int, TextBlockParam>();
        foreach (var page in pages)
        {
            if (!string.IsNullOrWhiteSpace(page.CanonicalText))
            {
                pageBlockCache[page.PageNumber] = new TextBlockParam(
                    $"<document>\n{page.CanonicalText}\n</document>")
                {
                    CacheControl = new CacheControlEphemeral(),
                };
            }
        }

        logger.LogInformation("Batched {ChunkCount} chunks into {PageCount} page-level requests",
            chunks.Count, chunksByPage.Count);

        // Map ChunkIndex → enriched chunk (thread-safe)
        var enrichedMap = new ConcurrentDictionary<int, DocumentChunk>();
        var successCount = 0;

        await Parallel.ForEachAsync(
            chunksByPage,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = ct },
            async (pageGroup, innerCt) =>
            {
                var pageNumber = pageGroup.Key;
                var pageChunks = pageGroup.Value;

                if (!pageBlockCache.TryGetValue(pageNumber, out var pageBlock))
                {
                    foreach (var chunk in pageChunks)
                        enrichedMap[chunk.ChunkIndex] = chunk;
                    return;
                }

                try
                {
                    var contexts = await GeneratePageContextsAsync(pageBlock, pageChunks, innerCt);

                    for (var i = 0; i < pageChunks.Count; i++)
                    {
                        var chunk = pageChunks[i];
                        if (i < contexts.Count && !string.IsNullOrWhiteSpace(contexts[i]))
                        {
                            enrichedMap[chunk.ChunkIndex] = chunk with
                            {
                                EnrichedText = $"{contexts[i]}\n\n{chunk.ChunkText}",
                            };
                            Interlocked.Increment(ref successCount);
                        }
                        else
                        {
                            enrichedMap[chunk.ChunkIndex] = chunk;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to enrich page {Page} ({Count} chunks), keeping originals",
                        pageNumber, pageChunks.Count);
                    foreach (var chunk in pageChunks)
                        enrichedMap[chunk.ChunkIndex] = chunk;
                }
            });

        logger.LogInformation("Contextual enrichment complete: {Success}/{Total} chunks enriched",
            successCount, chunks.Count);

        return chunks.Select(c => enrichedMap.GetValueOrDefault(c.ChunkIndex, c)).ToList();
    }

    private async Task<List<string>> GeneratePageContextsAsync(
        TextBlockParam cachedPageBlock, List<DocumentChunk> pageChunks, CancellationToken ct)
    {
        // Single chunk: use the simpler prompt (no parsing needed)
        var isSingle = pageChunks.Count == 1;

        var prompt = isSingle
            ? $"""
              Here is the chunk we want to situate within the document page:
              <chunk>
              {pageChunks[0].ChunkText}
              </chunk>
              Give a short succinct context (1-2 sentences) to situate this chunk within the document for improving search retrieval. Mention specific entity names, dates, and section topics. Answer only with the context, nothing else.
              """
            : BuildBatchPrompt(pageChunks);

        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = TokensPerChunk * pageChunks.Count,
            Messages =
            [
                new MessageParam
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>
                    {
                        cachedPageBlock,
                        new TextBlockParam(prompt),
                    },
                },
            ],
        };

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await AcquireRateLimitPermitAsync(ct);
                var response = await client.Messages.Create(parameters, ct);

                foreach (var block in response.Content)
                {
                    if (block.TryPickText(out var textBlock))
                    {
                        var text = textBlock.Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return isSingle
                                ? [text]
                                : ParseBatchResponse(text, pageChunks.Count);
                        }
                    }
                }

                return [];
            }
            catch (AnthropicRateLimitException) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                logger.LogDebug("Rate limited, retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(delay, ct);
            }
        }

        return [];
    }

    private static string BuildBatchPrompt(List<DocumentChunk> pageChunks)
    {
        var chunkList = string.Join("\n\n", pageChunks.Select((c, i) =>
            $"[{i + 1}]\n{c.ChunkText}"));

        return $"""
            Below are {pageChunks.Count} text chunks from this document page. For each chunk, generate a short context (1-2 sentences) that situates it within the document for search retrieval. Mention specific entity names, dates, and section topics.

            {chunkList}

            Respond with EXACTLY {pageChunks.Count} contexts using this format:
            [1] context for chunk 1
            [2] context for chunk 2
            Do not include anything else.
            """;
    }

    private async Task AcquireRateLimitPermitAsync(CancellationToken ct)
    {
        await _rateLimitGate.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequestTime;
            if (elapsed < MinRequestInterval)
                await Task.Delay(MinRequestInterval - elapsed, ct);
            _lastRequestTime = DateTime.UtcNow;
        }
        finally
        {
            _rateLimitGate.Release();
        }
    }

    internal static List<string> ParseBatchResponse(string response, int expectedCount)
    {
        var contexts = new string[expectedCount];

        // Match [N] prefix followed by context text
        var matches = BatchContextPattern().Matches(response);

        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out var idx) && idx >= 1 && idx <= expectedCount)
                contexts[idx - 1] = match.Groups[2].Value.Trim();
        }

        // Fallback: if regex parsing got nothing, try line-by-line
        if (contexts.All(c => c is null))
        {
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < Math.Min(lines.Length, expectedCount); i++)
                contexts[i] = StripPrefix().Replace(lines[i], "").Trim();
        }

        return [.. contexts.Select(c => c ?? "")];
    }

    [GeneratedRegex(@"\[(\d+)\]\s*(.+?)(?=\[\d+\]|\z)", RegexOptions.Singleline)]
    private static partial Regex BatchContextPattern();

    [GeneratedRegex(@"^\[\d+\]\s*")]
    private static partial Regex StripPrefix();
}

public record ContextualRetrievalOptions(
    bool Enabled = true,
    string Model = "claude-haiku-4-5-20251001");
