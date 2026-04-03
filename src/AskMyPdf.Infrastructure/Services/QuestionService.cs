namespace AskMyPdf.Infrastructure.Services;

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

        await foreach (var evt in claude.StreamAnswerAsync(question, pdfBytes, doc.FileName))
        {
            switch (evt)
            {
                case AnswerStreamEvent.TextDelta:
                    yield return evt;
                    break;

                case AnswerStreamEvent.CitationReceived { Citation: var citation }:
                    var highlightAreas = transformer.ToHighlightAreas(
                        citation.CitedText,
                        citation.PageNumber,
                        pageBounds);

                    yield return new AnswerStreamEvent.CitationReceived(
                        citation with
                        {
                            DocumentId = documentId,
                            HighlightAreas = highlightAreas,
                        });
                    break;

                case AnswerStreamEvent.Done:
                    yield return evt;
                    break;
            }
        }
    }
}
