namespace AskMyPdf.Infrastructure.Services;

using System.Diagnostics;
using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using Microsoft.Extensions.Logging;

public class DocumentService(
    BoundingBoxExtractor extractor,
    DocumentChunker chunker,
    EmbeddingService embeddings,
    DocumentRepository documents,
    ChunkRepository chunks,
    ILogger<DocumentService> logger)
{
    private const int MaxPageCount = 100;
    private const int EmbeddingBatchSize = 50;

    public async Task<Document> UploadAsync(byte[] pdfBytes, string fileName, long fileSize,
        CancellationToken ct = default)
    {
        var pages = extractor.ExtractPages(pdfBytes);

        if (pages.Count > MaxPageCount)
            throw new InvalidOperationException(
                $"PDF exceeds the {MaxPageCount}-page limit. Please upload a shorter document.");

        var doc = new Document(
            Id: Guid.NewGuid().ToString("N"),
            FileName: fileName,
            UploadedAt: DateTime.UtcNow,
            PageCount: pages.Count,
            FileSize: fileSize);

        await documents.SaveDocumentAsync(doc, pdfBytes, pages);

        // Index chunks for RAG engine
        var chunkList = chunker.ChunkDocument(doc.Id, pages);
        AssertChunkFindability(chunkList, pages);
        if (chunkList.Count > 0)
        {
            await chunks.SaveChunksAsync(doc.Id, chunkList);

            // Generate vector embeddings (if OpenAI is configured)
            if (embeddings.IsAvailable)
            {
                await GenerateAndSaveEmbeddingsAsync(doc.Id, chunkList, ct);
            }
            else
            {
                logger.LogInformation("Skipping embedding generation — OpenAI API key not configured. RAG will use FTS5-only retrieval.");
            }
        }

        return doc;
    }

    public Task<List<Document>> GetAllAsync() =>
        documents.GetAllDocumentsAsync();

    public Task<Document?> GetByIdAsync(string id) =>
        documents.GetDocumentAsync(id);

    public Task<byte[]?> GetFileAsync(string documentId) =>
        documents.GetFileAsync(documentId);

    public Task<bool> DeleteAsync(string id) =>
        documents.DeleteDocumentAsync(id);

    /// <summary>
    /// DEBUG-only: verifies every chunk is findable in its page's canonical text via dense matching.
    /// Elided entirely in Release builds by [Conditional].
    /// </summary>
    [Conditional("DEBUG")]
    private static void AssertChunkFindability(List<DocumentChunk> chunkList, List<PageCanonicalData> pages)
    {
        var pageMap = pages.ToDictionary(p => p.PageNumber);
        foreach (var chunk in chunkList)
        {
            if (!pageMap.TryGetValue(chunk.PageNumber, out var page)) continue;
            var chunkDense = CoordinateTransformer.ToDenseNormalized(chunk.ChunkText);
            var pageDense = CoordinateTransformer.ToDenseNormalized(page.CanonicalText);
            Debug.Assert(pageDense.Contains(chunkDense, StringComparison.Ordinal),
                $"Chunk {chunk.ChunkId} on page {chunk.PageNumber} not findable in canonical text");
        }
    }

    private async Task GenerateAndSaveEmbeddingsAsync(string documentId, List<DocumentChunk> chunkList,
        CancellationToken ct = default)
    {
        try
        {
            var allEmbeddings = new List<(int ChunkIndex, float[] Embedding)>();

            // Process in batches to avoid OpenAI rate limits
            for (var i = 0; i < chunkList.Count; i += EmbeddingBatchSize)
            {
                var batch = chunkList.Skip(i).Take(EmbeddingBatchSize).ToList();
                var texts = batch.Select(c => c.ChunkText).ToList();

                var vectors = await embeddings.GenerateEmbeddingsAsync(texts, ct);
                if (vectors is null)
                {
                    logger.LogWarning("Embedding generation failed for batch {BatchStart}-{BatchEnd}, skipping embeddings",
                        i, i + batch.Count);
                    return;
                }

                for (var j = 0; j < batch.Count; j++)
                {
                    allEmbeddings.Add((batch[j].ChunkIndex, vectors[j]));
                }
            }

            await chunks.SaveEmbeddingsAsync(documentId, allEmbeddings);
            logger.LogInformation("Generated and saved {Count} embeddings for document {DocId}",
                allEmbeddings.Count, documentId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate embeddings for document {DocId} — RAG will use FTS5-only", documentId);
        }
    }
}
