namespace AskMyPdf.Core.Services;

using AskMyPdf.Core.Models;

/// <summary>
/// Abstraction for a document Q&amp;A engine. Each implementation produces the same
/// stream of <see cref="AnswerStreamEvent"/>s so the downstream highlight pipeline
/// can process them uniformly.
/// </summary>
public interface IAnswerEngine
{
    /// <summary>Human-readable name shown in the UI (e.g. "RAG (Hybrid)").</summary>
    string DisplayName { get; }

    /// <summary>Wire key used in API requests (e.g. "rag", "claude-cli").</summary>
    string Key { get; }

    /// <summary>
    /// Produce raw answer events. Text deltas may arrive one-by-one (streaming) or
    /// as a single chunk (batch). Citations should carry the snippet text in
    /// <see cref="Citation.CitedText"/> with empty <see cref="Citation.HighlightAreas"/>
    /// — the shared pipeline fills those in.
    /// </summary>
    IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
        string question, byte[] pdfBytes, string fileName, string documentId,
        CancellationToken ct = default);
}
