namespace AskMyPdf.Infrastructure.Ai;

using Anthropic;
using Anthropic.Models.Messages;
using AskMyPdf.Core.Models;
using Microsoft.Extensions.Logging;

public class ClaudeService(AnthropicClient client, ILogger<ClaudeService> logger)
{
    private const string GroundingPrompt = """
        You are a document Q&A assistant. Answer the user's question based ONLY on the provided document.

        Rules:
        1. Only use information explicitly stated in the document
        2. If the document does not contain the answer, say: "I could not find an answer to this question in the provided document."
        3. Quote relevant text to support your answer
        4. If multiple sections are relevant, reference all of them
        5. Never make up or infer information beyond what is explicitly stated
        6. Be concise and direct
        """;

    public async IAsyncEnumerable<AnswerStreamEvent> StreamAnswerAsync(
        string question,
        byte[] pdfBytes,
        string fileName)
    {
        var pdfBase64 = Convert.ToBase64String(pdfBytes);

        var parameters = new MessageCreateParams
        {
            Model = "claude-sonnet-4-5-20250514",
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
}
