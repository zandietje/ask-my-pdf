namespace AskMyPdf.Core.Models;

public record DocumentChunk(
    string DocumentId,
    int PageNumber,
    int ChunkIndex,
    string ChunkText)
{
    /// <summary>Short ID for LLM context (e.g. "C3"). Used in chunk-ID citation format.</summary>
    public string ChunkId => $"C{ChunkIndex}";
}
