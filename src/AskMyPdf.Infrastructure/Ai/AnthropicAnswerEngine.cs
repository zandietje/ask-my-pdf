namespace AskMyPdf.Infrastructure.Ai;

using System.Runtime.CompilerServices;
using AskMyPdf.Core.Models;
using AskMyPdf.Core.Services;

/// <summary>
/// Engine A — sends the full PDF to the Claude API with citations enabled.
/// Real-time text streaming, broad page-level citations that need focusing.
/// </summary>
public class AnthropicAnswerEngine(ClaudeService claude) : IAnswerEngine
{
    public string DisplayName => "Anthropic API";
    public string Key => "anthropic";
    public bool NeedsFocusing => true;

    public async IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
        string question, byte[] pdfBytes, string fileName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in claude.StreamAnswerAsync(question, pdfBytes, fileName, ct))
        {
            yield return evt;
        }
    }
}
