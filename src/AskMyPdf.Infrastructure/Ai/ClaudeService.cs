namespace AskMyPdf.Infrastructure.Ai;

using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Models.Messages;
using AskMyPdf.Core.Models;
using Microsoft.Extensions.Logging;

public class ClaudeService(AnthropicClient client, ClaudeServiceOptions options, ILogger<ClaudeService> logger)
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
        You are given a passage from a document page. A user asked a question and an AI produced an answer based on this passage.

        Question: {{QUESTION}}
        Answer: {{ANSWER}}

        Your task is to extract the exact parts of the passage that directly support the answer.

        Instructions:

        1. Find ALL parts of the passage that were used to produce the answer — not just one, but every sentence, data row, or fragment that contributed
        2. Copy each part character-for-character from the passage — do not paraphrase or modify wording
        3. Return each extracted part on a new line
        4. Do NOT include text that is unrelated to the answer
        5. If the passage contains no information that supports the answer, return exactly: NO_MATCH
        """;

    public async IAsyncEnumerable<AnswerStreamEvent> StreamAnswerAsync(
        string question,
        byte[] pdfBytes,
        string fileName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var pdfBase64 = Convert.ToBase64String(pdfBytes);

        var parameters = new MessageCreateParams
        {
            Model = options.AnswerModel,
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

        await foreach (var evt in client.Messages.CreateStreaming(parameters).WithCancellation(ct))
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
    /// Extracts the precise supporting span from a broad page citation using the configured focus model.
    /// Returns null if the call fails or the result can't be validated — caller should fall back.
    /// </summary>
    public async Task<string?> FocusCitationAsync(
        string citedText, string question, string fullAnswer,
        CancellationToken ct = default)
    {
        try
        {
            var systemPrompt = FocusPromptTemplate
                .Replace("{{QUESTION}}", question)
                .Replace("{{ANSWER}}", fullAnswer);

            var parameters = new MessageCreateParams
            {
                Model = options.FocusModel,
                MaxTokens = 500,
                System = systemPrompt,
                Messages =
                [
                    new MessageParam
                    {
                        Role = Role.User,
                        Content = new List<ContentBlockParam>
                        {
                            new TextBlockParam(citedText),
                        },
                    },
                ],
            };

            var response = await client.Messages.Create(parameters, cancellationToken: ct);

            var rawText = string.Concat(
                response.Content
                    .Select(block => block.TryPickText(out var tb) ? tb.Text : ""));

            var resultText = ParseFocusResponse(rawText);
            if (resultText is null)
            {
                logger.LogInformation("Focus returned no usable result (raw: {RawLen} chars)",
                    rawText?.Length ?? 0);
                return null;
            }

            logger.LogInformation("Focus returned {FocusLen} chars from page ({PageLen} chars)",
                resultText.Length, citedText.Length);
            return resultText;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Citation focus call failed");
            return null;
        }
    }

    /// <summary>
    /// Parses raw LLM output from the focus call into cleaned text.
    /// Returns null for empty, whitespace-only, or NO_MATCH responses.
    /// Strips surrounding quotes the model sometimes adds.
    /// </summary>
    internal static string? ParseFocusResponse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        var text = rawText.Trim();

        if (text.Equals("NO_MATCH", StringComparison.OrdinalIgnoreCase))
            return null;

        if ((text.StartsWith('"') && text.EndsWith('"')) ||
            (text.StartsWith('\u201C') && text.EndsWith('\u201D')))
            text = text[1..^1].Trim();

        return text;
    }
}
