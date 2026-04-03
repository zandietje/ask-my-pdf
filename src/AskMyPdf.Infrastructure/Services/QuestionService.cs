namespace AskMyPdf.Infrastructure.Services;

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
        string documentId)
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

        var pageBounds = await db.GetPageBoundsAsync(documentId);

        logger.LogInformation("Streaming answer for document {FileName} ({DocumentId})", doc.FileName, documentId);

        // Phase 1: Stream text deltas immediately for real-time UX.
        //          Collect citations — we need the full answer before we can focus them.
        var fullAnswer = new StringBuilder();
        var pendingCitations = new List<Citation>();

        await foreach (var evt in claude.StreamAnswerAsync(question, pdfBytes, doc.FileName))
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

        // Phase 2: Now we have the complete answer. Focus each citation with full context.
        var completeAnswer = fullAnswer.ToString();
        logger.LogInformation("Answer complete ({AnswerLen} chars), processing {Count} citations",
            completeAnswer.Length, pendingCitations.Count);

        foreach (var citation in pendingCitations)
        {
            var focusedText = citation.CitedText;

            if (citation.CitedText.Length > 200)
            {
                var page = pageBounds.FirstOrDefault(p => p.PageNumber == citation.PageNumber);
                var pageText = page is not null
                    ? CoordinateTransformer.ReconstructPageText(page)
                    : citation.CitedText;

                logger.LogInformation(
                    "Focusing citation page {Page}: citedText={CitedLen} chars, pageText={PageLen} chars",
                    citation.PageNumber, citation.CitedText.Length, pageText.Length);

                var sonnetResult = await claude.FocusCitationAsync(
                    pageText, question, completeAnswer);

                if (sonnetResult is not null)
                {
                    focusedText = sonnetResult;
                    logger.LogInformation("Focus succeeded: {FocusLen} chars", focusedText.Length);
                }
                else
                {
                    logger.LogWarning("Focus returned null — using original citedText ({Len} chars)",
                        citation.CitedText.Length);
                }
            }

            var highlightAreas = transformer.ToHighlightAreas(
                focusedText, citation.PageNumber, pageBounds);

            logger.LogDebug(
                "Citation page={Page}: focused {OrigLen}→{FocusLen} chars, {Count} highlight areas",
                citation.PageNumber, citation.CitedText.Length, focusedText.Length, highlightAreas.Count);

            yield return new AnswerStreamEvent.CitationReceived(
                citation with
                {
                    DocumentId = documentId,
                    CitedText = focusedText,
                    HighlightAreas = highlightAreas,
                });
        }

        yield return new AnswerStreamEvent.Done();
    }
}
