namespace AskMyPdf.Infrastructure.Ai;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;
using AskMyPdf.Core.Models;
using AskMyPdf.Core.Services;
using AskMyPdf.Infrastructure.Data;
using Microsoft.Extensions.Logging;

/// <summary>
/// Engine C — Hybrid RAG engine. Retrieves relevant chunks via FTS5 + vector search
/// (reciprocal rank fusion), then streams a grounded answer from Claude with inline
/// chunk-ID citations ([C3]) that map deterministically to page highlights.
/// </summary>
public class RagAnswerEngine(
    SqliteDb db,
    AnthropicClient client,
    EmbeddingService embeddings,
    RagEngineOptions options,
    ILogger<RagAnswerEngine> logger) : IAnswerEngine
{
    public string DisplayName => "RAG (Hybrid)";
    public string Key => "rag";
    public bool NeedsFocusing => false;

    private static readonly Regex ChunkCitationPattern = new(@"\[C(\d+)\]", RegexOptions.Compiled);
    private const int RrfK = 60; // Reciprocal rank fusion constant

    public async IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
        string question, byte[] pdfBytes, string fileName, string documentId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Retrieve relevant chunks via hybrid search
        var allChunks = await db.GetAllChunksAsync(documentId);
        if (allChunks.Count == 0)
        {
            yield return new AnswerStreamEvent.TextDelta(
                "No indexed content found for this document. The RAG engine requires document chunks to be indexed during upload.");
            yield return new AnswerStreamEvent.Done();
            yield break;
        }

        var chunkMap = allChunks.ToDictionary(c => c.ChunkIndex);
        var retrieved = await HybridRetrieveAsync(documentId, question, allChunks, ct);

        if (retrieved.Count == 0)
        {
            yield return new AnswerStreamEvent.TextDelta(
                "I could not find relevant content in this document for your question.");
            yield return new AnswerStreamEvent.Done();
            yield break;
        }

        // 2. Add context window: include adjacent chunks for surrounding context
        var contextChunks = ExpandWithContext(retrieved, chunkMap);

        logger.LogInformation(
            "RAG: retrieved {RetrievedCount} chunks (expanded to {ContextCount} with context) for document {DocId}, pages: {Pages}",
            retrieved.Count, contextChunks.Count, documentId,
            string.Join(", ", contextChunks.Select(c => c.PageNumber).Distinct().Order()));

        // 3. Build context block with chunk IDs
        var context = BuildContext(contextChunks);

        // 4. Stream answer from Claude
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = 4096,
            System = BuildSystemPrompt(),
            Messages =
            [
                new MessageParam
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>
                    {
                        new TextBlockParam($"DOCUMENT EXCERPTS:\n\n{context}"),
                        new TextBlockParam($"QUESTION: {question}"),
                    },
                },
            ],
        };

        var fullAnswer = new StringBuilder();

        await foreach (var streamEvent in client.Messages.CreateStreaming(parameters).WithCancellation(ct))
        {
            if (streamEvent.TryPickContentBlockDelta(out var deltaEvt)
                && deltaEvt.Delta.TryPickText(out var textDelta))
            {
                fullAnswer.Append(textDelta.Text);
                yield return new AnswerStreamEvent.TextDelta(textDelta.Text);
            }
        }

        // 5. Extract citations from inline [C<n>] references
        var citations = ExtractCitations(fullAnswer.ToString(), chunkMap, fileName, documentId);

        logger.LogInformation("RAG: extracted {Count} chunk-ID citations from answer", citations.Count);

        foreach (var citation in citations)
        {
            yield return new AnswerStreamEvent.CitationReceived(citation);
        }

        yield return new AnswerStreamEvent.Done();
    }

    /// <summary>
    /// Hybrid retrieval: runs FTS5 (lexical) and vector search (semantic) in parallel,
    /// merges results with reciprocal rank fusion. Falls back to FTS5-only if no embeddings.
    /// </summary>
    private async Task<List<DocumentChunk>> HybridRetrieveAsync(
        string documentId, string question, List<DocumentChunk> allChunks, CancellationToken ct)
    {
        var topK = options.TopK;

        // FTS5 search
        var ftsTask = db.SearchChunksAsync(documentId, question, topK * 2);

        // Vector search (if embeddings are available)
        Task<List<(int ChunkIndex, double Score)>>? vectorTask = null;
        if (embeddings.IsAvailable && await db.HasEmbeddingsAsync(documentId))
        {
            var queryEmbedding = await embeddings.GenerateEmbeddingAsync(question, ct);
            if (queryEmbedding is not null)
                vectorTask = db.VectorSearchAsync(documentId, queryEmbedding, topK * 2);
        }

        var ftsResults = await ftsTask;
        var vectorResults = vectorTask is not null ? await vectorTask : null;

        if (vectorResults is not null && vectorResults.Count > 0)
        {
            // Hybrid: merge with Reciprocal Rank Fusion
            var merged = ReciprocalRankFusion(ftsResults, vectorResults, allChunks);
            logger.LogInformation("RAG: hybrid retrieval — FTS5={FtsCount}, Vector={VecCount}, merged={MergedCount}",
                ftsResults.Count, vectorResults.Count, merged.Count);
            return merged.Take(topK).ToList();
        }

        // FTS5-only fallback
        if (ftsResults.Count > 0)
        {
            logger.LogInformation("RAG: FTS5-only retrieval — {Count} results", ftsResults.Count);
            return ftsResults.Take(topK).ToList();
        }

        // Last resort: first N chunks
        logger.LogInformation("RAG: no search results, falling back to first {TopK} chunks", topK);
        return allChunks.Take(topK).ToList();
    }

    /// <summary>
    /// Reciprocal Rank Fusion: merges two ranked lists into one.
    /// RRF_score(d) = sum(1 / (k + rank_i(d))) for each retrieval system i.
    /// </summary>
    private List<DocumentChunk> ReciprocalRankFusion(
        List<DocumentChunk> ftsResults,
        List<(int ChunkIndex, double Score)> vectorResults,
        List<DocumentChunk> allChunks)
    {
        var chunkMap = allChunks.ToDictionary(c => c.ChunkIndex);
        var scores = new Dictionary<int, double>();

        // FTS5 ranks (already ordered by BM25)
        for (var rank = 0; rank < ftsResults.Count; rank++)
        {
            var idx = ftsResults[rank].ChunkIndex;
            scores[idx] = scores.GetValueOrDefault(idx) + 1.0 / (RrfK + rank + 1);
        }

        // Vector ranks (already ordered by cosine similarity)
        for (var rank = 0; rank < vectorResults.Count; rank++)
        {
            var idx = vectorResults[rank].ChunkIndex;
            scores[idx] = scores.GetValueOrDefault(idx) + 1.0 / (RrfK + rank + 1);
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Where(kv => chunkMap.ContainsKey(kv.Key))
            .Select(kv => chunkMap[kv.Key])
            .ToList();
    }

    /// <summary>
    /// Expands retrieved chunks with adjacent context (one chunk before and after each).
    /// This gives Claude surrounding context without expanding the retrieval set too broadly.
    /// </summary>
    private static List<DocumentChunk> ExpandWithContext(
        List<DocumentChunk> retrieved, Dictionary<int, DocumentChunk> chunkMap)
    {
        var includeIndices = new HashSet<int>();
        foreach (var chunk in retrieved)
        {
            includeIndices.Add(chunk.ChunkIndex);
            if (chunkMap.ContainsKey(chunk.ChunkIndex - 1))
                includeIndices.Add(chunk.ChunkIndex - 1);
            if (chunkMap.ContainsKey(chunk.ChunkIndex + 1))
                includeIndices.Add(chunk.ChunkIndex + 1);
        }

        return includeIndices
            .Order()
            .Where(chunkMap.ContainsKey)
            .Select(idx => chunkMap[idx])
            .ToList();
    }

    private static string BuildContext(List<DocumentChunk> chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks.OrderBy(c => c.ChunkIndex))
        {
            sb.AppendLine($"--- [{chunk.ChunkId}, Page {chunk.PageNumber}] ---");
            sb.AppendLine(chunk.ChunkText);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildSystemPrompt() => """
        You are a document Q&A assistant. Answer the user's question based ONLY on the provided document excerpts.

        CRITICAL — LANGUAGE RULE: Your ENTIRE response MUST be in the SAME language as the user's question. If the user writes in Dutch, answer ENTIRELY in Dutch. If in English, answer in English. Never mix languages.

        Citation rules:
        1. For every factual claim, cite the source chunk using its ID in square brackets, e.g. [C3].
           Example: "Revenue grew by 15% year-over-year [C3] while operating expenses decreased [C7]."
        2. You MUST cite at least one chunk for every factual statement.
        3. Do NOT copy or quote text from the excerpts. Just reference the chunk ID. The system handles highlighting.
        4. If the excerpts do not contain the answer, respond exactly:
           "I could not find an answer to this question in the provided document."
           Do NOT cite any chunks in that case.

        Answer rules:
        1. Only use information explicitly stated in the excerpts.
        2. Be concise and direct.
        3. If multiple excerpts are relevant, cite each one where appropriate.
        4. Never make up or infer information beyond what is explicitly stated.
        """;

    /// <summary>
    /// Extracts inline [C&lt;n&gt;] chunk-ID references from the answer and maps them
    /// to citations using the original chunk text for deterministic highlight matching.
    /// With small chunks (~150 chars ≈ 1-2 sentences), the full chunk text IS the
    /// precise citation — no focusing needed.
    /// </summary>
    internal static List<Core.Models.Citation> ExtractCitations(
        string answer, Dictionary<int, DocumentChunk> chunkMap, string fileName, string documentId)
    {
        var seen = new HashSet<int>();
        var citations = new List<Core.Models.Citation>();

        foreach (Match match in ChunkCitationPattern.Matches(answer))
        {
            if (!int.TryParse(match.Groups[1].Value, out var chunkIndex))
                continue;
            if (!seen.Add(chunkIndex)) // deduplicate
                continue;
            if (!chunkMap.TryGetValue(chunkIndex, out var chunk))
                continue;

            citations.Add(new Core.Models.Citation(
                DocumentId: documentId,
                DocumentName: fileName,
                PageNumber: chunk.PageNumber,
                CitedText: chunk.ChunkText,
                HighlightAreas: [],
                ChunkIndex: chunkIndex));
        }

        return citations;
    }
}

public record RagEngineOptions(
    string Model = "claude-sonnet-4-20250514",
    int TopK = 8);
