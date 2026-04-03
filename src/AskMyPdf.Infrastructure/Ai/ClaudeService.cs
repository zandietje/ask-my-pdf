namespace AskMyPdf.Infrastructure.Ai;

using Anthropic;
using Anthropic.Models.Messages;
using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Pdf;
using Microsoft.Extensions.Logging;

public class ClaudeService(AnthropicClient client, ILogger<ClaudeService> logger)
{
    private const string GroundingPrompt = """
        You are a document Q&A assistant. Answer the user's question based ONLY on the provided document.

        CRITICAL — LANGUAGE RULE: Your ENTIRE response MUST be in the SAME language as the user's question. If the user writes in Dutch, answer ENTIRELY in Dutch — every word, including introductory phrases like "Based on", "The document states", etc. If in English, answer in English. Never mix languages. This rule overrides everything else.

        Rules:
        1. Only use information explicitly stated in the document
        2. If the document does not contain the answer, say so in the user's language (e.g. Dutch: "Ik kon geen antwoord op deze vraag vinden in het document.")
        3. When referencing the document, cite the SINGLE MOST RELEVANT SENTENCE — ideally under 100 words. Never cite an entire paragraph, section header + body, or bullet list. If you need multiple facts, make multiple separate citations.
        4. Break your answer into multiple short statements, each citing its own specific passage, rather than one long statement citing a large block.
        5. If multiple sections are relevant, reference each one separately with its own focused citation.
        6. Never make up or infer information beyond what is explicitly stated.
        7. Be concise and direct.
        """;

    private const string FocusPromptTemplate = """
        You are given a passage from a document page. The user asked a question and received an answer.

        Question: {0}
        Answer: {1}

        Your task: identify which sentence(s) from the passage were used to produce the answer.

        Rules:
        Return ONLY the exact, verbatim sentence(s) copied character-for-character from the passage that were used to create the answer
        
        """;

    public async IAsyncEnumerable<AnswerStreamEvent> StreamAnswerAsync(
        string question,
        byte[] pdfBytes,
        string fileName)
    {
        var pdfBase64 = Convert.ToBase64String(pdfBytes);

        var parameters = new MessageCreateParams
        {
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 4096,
            System = GroundingPrompt,
            Messages =
            [
                new MessageParam
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>
                    {
                        new DocumentBlockParam(new Base64PdfSource(data: pdfBase64))
                        {
                            Title = fileName,
                            Citations = new CitationsConfigParam { Enabled = true },
                            CacheControl = new CacheControlEphemeral(),
                        },
                        new TextBlockParam(question),
                    },
                },
            ],
        };

        logger.LogInformation("Sending question to Claude for document {FileName}", fileName);

        await foreach (var evt in client.Messages.CreateStreaming(parameters))
        {
            if (evt.TryPickContentBlockDelta(out var deltaEvt))
            {
                if (deltaEvt.Delta.TryPickText(out var textDelta))
                {
                    yield return new AnswerStreamEvent.TextDelta(textDelta.Text);
                }
                else if (deltaEvt.Delta.TryPickCitations(out var citDelta))
                {
                    if (citDelta.Citation.TryPickPageLocation(out var pageLoc))
                    {
                        yield return new AnswerStreamEvent.CitationReceived(
                            new Core.Models.Citation(
                                DocumentId: "",
                                DocumentName: fileName,
                                PageNumber: (int)pageLoc.StartPageNumber,
                                CitedText: pageLoc.CitedText,
                                HighlightAreas: []));
                    }
                }
            }
        }

        yield return new AnswerStreamEvent.Done();
    }

    /// <summary>
    /// Uses Sonnet to extract the precise span from a large citation that supports the answer.
    /// Returns null if the call fails or the result can't be validated — caller should fall back.
    /// </summary>
    public async Task<string?> FocusCitationAsync(
        string citedText, string question, string fullAnswer)
    {
        try
        {
            var userPrompt = $"""
                {citedText}
                """;

            var systemPrompt = string.Format(FocusPromptTemplate, question, fullAnswer);

            var parameters = new MessageCreateParams
            {
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 500,
                System = systemPrompt,
                Messages =
                [
                    new MessageParam
                    {
                        Role = Role.User,
                        Content = new List<ContentBlockParam>
                        {
                            new TextBlockParam(userPrompt),
                        },
                    },
                ],
            };

            var response = await client.Messages.Create(parameters);

            // Extract text from response content blocks
            var resultText = string.Concat(
                response.Content
                    .Select(block => block.TryPickText(out var tb) ? tb.Text : ""));

            if (string.IsNullOrWhiteSpace(resultText))
                return null;

            resultText = resultText.Trim();

            // Strip surrounding quotes that Sonnet sometimes adds despite instructions
            if ((resultText.StartsWith('"') && resultText.EndsWith('"')) ||
                (resultText.StartsWith('\u201C') && resultText.EndsWith('\u201D')))
                resultText = resultText[1..^1].Trim();

            // Validate: result must exist as a dense substring of the source text
            var denseResult = CoordinateTransformer.ToDense(resultText);
            var denseCited = CoordinateTransformer.ToDense(citedText);

            if (denseResult.Length >= 10 && denseCited.Contains(denseResult, StringComparison.Ordinal))
            {
                logger.LogInformation("Focus narrowed citation: {OrigLen}→{FocusLen} chars",
                    citedText.Length, resultText.Length);
                return resultText;
            }

            logger.LogWarning(
                "Focus result NOT validated ({ResultLen} chars vs source {CitedLen} chars). First 100 chars of result: {Preview}",
                resultText.Length, citedText.Length,
                resultText.Length > 100 ? resultText[..100] : resultText);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Citation focus call failed");
            return null;
        }
    }
}
