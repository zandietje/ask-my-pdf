namespace AskMyPdf.Infrastructure.Services;

using System.Runtime.CompilerServices;
using System.Text;
using AskMyPdf.Core.Models;
using AskMyPdf.Core.Services;
using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using Microsoft.Extensions.Logging;

public class QuestionService(
    IEnumerable<IAnswerEngine> engines,
    ClaudeService claude,
    SqliteDb db,
    CoordinateTransformer transformer,
    ILogger<QuestionService> logger)
{
    private const string DefaultEngine = "anthropic";

    public async IAsyncEnumerable<AnswerStreamEvent> StreamAnswerAsync(
        string question,
        string documentId,
        string? engineKey = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var engine = ResolveEngine(engineKey);
        logger.LogInformation("Using engine {Engine} for document {DocumentId}", engine.Key, documentId);

        var doc = await db.GetDocumentAsync(documentId);
        if (doc is null)
        {
            logger.LogWarning("Document {DocumentId} not found", documentId);
            yield return new AnswerStreamEvent.TextDelta("Document not found.");
            yield return new AnswerStreamEvent.Done();
            yield break;
        }

        var pdfBytes = await db.GetFileAsync(documentId);
        if (pdfBytes is null)
        {
            logger.LogWarning("File for document {DocumentId} not found", documentId);
            yield return new AnswerStreamEvent.TextDelta("Document file not found.");
            yield return new AnswerStreamEvent.Done();
            yield break;
        }

        logger.LogInformation("Streaming answer for document {FileName} ({DocumentId})", doc.FileName, documentId);

        // Phase 1: Stream from engine — forward text deltas, collect citations.
        var fullAnswer = new StringBuilder();
        var pendingCitations = new List<Citation>();

        await foreach (var evt in engine.StreamRawAnswerAsync(question, pdfBytes, doc.FileName, documentId, ct))
        {
            switch (evt)
            {
                case AnswerStreamEvent.TextDelta { Text: var text }:
                    fullAnswer.Append(text);
                    yield return evt;
                    break;

                case AnswerStreamEvent.CitationReceived { Citation: var citation }:
                    pendingCitations.Add(citation);
                    break;

                case AnswerStreamEvent.Done:
                    break; // Don't yield Done yet — process citations first
            }
        }

        // Phase 2: Resolve citations → bounding boxes.
        //   If the engine needs focusing (Anthropic), run focus calls first.
        //   Then resolve all citations to highlight areas.
        var completeAnswer = fullAnswer.ToString();
        var citedPages = pendingCitations
            .Select(c => c.PageNumber)
            .Distinct()
            .Order()
            .ToList();

        logger.LogInformation(
            "Answer complete ({AnswerLen} chars), {CitationCount} citations across {PageCount} pages [engine={Engine}]",
            completeAnswer.Length, pendingCitations.Count, citedPages.Count, engine.Key);

        if (citedPages.Count > 0)
        {
            var pageBounds = await db.GetPageBoundsAsync(documentId, citedPages);

            if (engine.NeedsFocusing)
            {
                // Anthropic path: broad page citations → focus (Haiku) → bounding boxes
                var focusTasks = citedPages.Select(async pageNumber =>
                {
                    var page = pageBounds.FirstOrDefault(p => p.PageNumber == pageNumber);
                    if (page is null || page.Words.Count == 0)
                        return (Citation?)null;

                    var pageText = CoordinateTransformer.ReconstructPageText(page);
                    var focusedText = await claude.FocusCitationAsync(
                        pageText, question, completeAnswer, ct);

                    if (focusedText is null)
                    {
                        logger.LogInformation("Page {Page}: NO_MATCH — no clear source text found", pageNumber);
                        return null;
                    }

                    var highlightAreas = transformer.ToHighlightAreas(
                        focusedText, pageNumber, pageBounds);

                    logger.LogInformation("Page {Page}: focused to {FocusLen} chars, {AreaCount} highlight areas",
                        pageNumber, focusedText.Length, highlightAreas.Count);

                    return new Citation(
                        DocumentId: documentId,
                        DocumentName: doc.FileName,
                        PageNumber: pageNumber,
                        CitedText: focusedText,
                        HighlightAreas: highlightAreas);
                }).ToList();

                var results = await Task.WhenAll(focusTasks);

                foreach (var citation in results)
                {
                    if (citation is not null)
                        yield return new AnswerStreamEvent.CitationReceived(citation);
                }
            }
            else
            {
                // CLI/RAG path: already-exact snippets → contiguous matching only
                // (no per-word fallback, which would scatter highlights across the page)
                foreach (var rawCitation in pendingCitations.OrderBy(c => c.PageNumber))
                {
                    var highlightAreas = transformer.ToHighlightAreas(
                        rawCitation.CitedText, rawCitation.PageNumber, pageBounds,
                        contiguousOnly: true);

                    logger.LogInformation("Page {Page}: {AreaCount} highlight areas (no focus needed)",
                        rawCitation.PageNumber, highlightAreas.Count);

                    yield return new AnswerStreamEvent.CitationReceived(
                        new Citation(
                            DocumentId: documentId,
                            DocumentName: doc.FileName,
                            PageNumber: rawCitation.PageNumber,
                            CitedText: rawCitation.CitedText,
                            HighlightAreas: highlightAreas,
                            ChunkIndex: rawCitation.ChunkIndex));
                }
            }
        }

        yield return new AnswerStreamEvent.Done();
    }

    private IAnswerEngine ResolveEngine(string? key)
    {
        var target = string.IsNullOrWhiteSpace(key) ? DefaultEngine : key;
        return engines.FirstOrDefault(e => e.Key.Equals(target, StringComparison.OrdinalIgnoreCase))
            ?? engines.First(e => e.Key.Equals(DefaultEngine, StringComparison.OrdinalIgnoreCase));
    }
}
