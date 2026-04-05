namespace AskMyPdf.Infrastructure.Services;

using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using Microsoft.Extensions.Logging;

public class DocumentService(
    BoundingBoxExtractor extractor,
    DocumentChunker chunker,
    EmbeddingService embeddings,
    SqliteDb db,
    ILogger<DocumentService> logger)
{
    private const int MaxPageCount = 100;
    private const int EmbeddingBatchSize = 50;

    public async Task<Document> UploadAsync(byte[] pdfBytes, string fileName, long fileSize)
    {
        var pages = extractor.ExtractWordBounds(pdfBytes);

        if (pages.Count > MaxPageCount)
            throw new InvalidOperationException(
                $"PDF exceeds the {MaxPageCount}-page limit. Please upload a shorter document.");

        var doc = new Document(
            Id: Guid.NewGuid().ToString("N"),
            FileName: fileName,
            UploadedAt: DateTime.UtcNow,
            PageCount: pages.Count,
            FileSize: fileSize);

        await db.SaveDocumentAsync(doc, pdfBytes, pages);

        // Index chunks for RAG engine
        var chunks = chunker.ChunkDocument(doc.Id, pages);
        if (chunks.Count > 0)
        {
            await db.SaveChunksAsync(doc.Id, chunks);

            // Generate vector embeddings (if OpenAI is configured)
            if (embeddings.IsAvailable)
            {
                await GenerateAndSaveEmbeddingsAsync(doc.Id, chunks);
            }
            else
            {
                logger.LogInformation("Skipping embedding generation — OpenAI API key not configured. RAG will use FTS5-only retrieval.");
            }
        }

        return doc;
    }

    private async Task GenerateAndSaveEmbeddingsAsync(string documentId, List<DocumentChunk> chunks)
    {
        try
        {
            var allEmbeddings = new List<(int ChunkIndex, float[] Embedding)>();

            // Process in batches to avoid OpenAI rate limits
            for (var i = 0; i < chunks.Count; i += EmbeddingBatchSize)
            {
                var batch = chunks.Skip(i).Take(EmbeddingBatchSize).ToList();
                var texts = batch.Select(c => c.ChunkText).ToList();

                var vectors = await embeddings.GenerateEmbeddingsAsync(texts);
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

            await db.SaveEmbeddingsAsync(documentId, allEmbeddings);
            logger.LogInformation("Generated and saved {Count} embeddings for document {DocId}",
                allEmbeddings.Count, documentId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate embeddings for document {DocId} — RAG will use FTS5-only", documentId);
        }
    }
}
