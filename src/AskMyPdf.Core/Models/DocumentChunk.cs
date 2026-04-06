namespace AskMyPdf.Core.Models;

public record DocumentChunk(
    string DocumentId,
    int PageNumber,
    int ChunkIndex,
    string ChunkText,
    string? EnrichedText = null)
{
    /// <summary>Short ID for LLM context (e.g. "C3"). Used in chunk-ID citation format.</summary>
    public string ChunkId => $"C{ChunkIndex}";

    /// <summary>
    /// Text used for embedding and FTS5 search (enriched if available, otherwise raw).
    /// </summary>
    public string SearchableText => EnrichedText ?? ChunkText;
}
