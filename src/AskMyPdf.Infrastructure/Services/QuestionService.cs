namespace AskMyPdf.Infrastructure.Services;

using System.Runtime.CompilerServices;
using System.Text;
using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using Microsoft.Extensions.Logging;

public class QuestionService(
    ClaudeService claude,
    SqliteDb db,
    CoordinateTransformer transformer,
    ILogger<QuestionService> logger)
{
    public async IAsyncEnumerable<AnswerStreamEvent> StreamAnswerAsync(
        string question,
        string documentId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
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

        // Phase 1: Stream text deltas immediately for real-time UX.
        //          Collect citations — we need the full answer before we can focus them.
        var fullAnswer = new StringBuilder();
        var pendingCitations = new List<Citation>();

        await foreach (var evt in claude.StreamAnswerAsync(question, pdfBytes, doc.FileName, ct))
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
                    // Don't yield Done yet — process citations first
                    break;
            }
        }

        // Phase 2: Group citations by page, parallelize focus calls.
        var completeAnswer = fullAnswer.ToString();
        var citedPages = pendingCitations
            .Select(c => c.PageNumber)
            .Distinct()
            .Order()
            .ToList();

        logger.LogInformation("Answer complete ({AnswerLen} chars), {CitationCount} citations across {PageCount} pages",
            completeAnswer.Length, pendingCitations.Count, citedPages.Count);

        if (citedPages.Count > 0)
        {
            // Load bounding data only for cited pages
            var pageBounds = await db.GetPageBoundsAsync(documentId, citedPages);

            // Fire all focus calls in parallel
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

            // Yield in page order (citedPages is already sorted, Task.WhenAll preserves order)
            foreach (var citation in results)
            {
                if (citation is not null)
                    yield return new AnswerStreamEvent.CitationReceived(citation);
            }
        }

        yield return new AnswerStreamEvent.Done();
    }
}
