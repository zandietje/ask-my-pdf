namespace AskMyPdf.Infrastructure.Ai;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskMyPdf.Core.Models;
using AskMyPdf.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Engine B — runs Claude Code CLI on the server to read the PDF from disk
/// and answer with structured evidence. Returns a batch result (no real-time streaming).
/// </summary>
public class ClaudeCliEngine(ClaudeCliRunner runner, ILogger<ClaudeCliEngine> logger) : IAnswerEngine
{
    public string DisplayName => "Claude Code CLI";
    public string Key => "claude-cli";
    public bool NeedsFocusing => false; // CLI prompt already produces exact snippets

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public async IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
        string question, byte[] pdfBytes, string fileName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Materialize PDF to a temp file so the CLI can read it
        var tempDir = Path.Combine(Path.GetTempPath(), "askmypdf");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.pdf");

        try
        {
            await File.WriteAllBytesAsync(tempPath, pdfBytes, ct);
            logger.LogInformation("Wrote temp PDF for CLI: {Path} ({Size} bytes)", tempPath, pdfBytes.Length);

            var prompt = BuildPrompt(tempPath, question);
            var result = await runner.RunAsync(prompt, workingDirectory: tempDir, ct: ct);

            if (!result.Success || result.ResultText is null)
            {
                var errorMsg = result.Error ?? "Claude Code CLI did not return a result.";
                logger.LogWarning("CLI engine failed: {Error}", errorMsg);
                yield return new AnswerStreamEvent.TextDelta($"Error: {errorMsg}");
                yield return new AnswerStreamEvent.Done();
                yield break;
            }

            var parsed = ParseStructuredResult(result.ResultText);
            if (parsed is null)
            {
                logger.LogWarning("Could not parse structured JSON from CLI result");
                // Fall back: return the raw text as the answer, no citations
                yield return new AnswerStreamEvent.TextDelta(result.ResultText);
                yield return new AnswerStreamEvent.Done();
                yield break;
            }

            // Yield the full answer as a single text delta
            yield return new AnswerStreamEvent.TextDelta(parsed.Answer);

            // Yield each evidence item as a raw citation (no highlight areas yet)
            foreach (var evidence in parsed.Evidence)
            {
                var combinedSnippet = string.Join("\n", evidence.Snippets);
                if (string.IsNullOrWhiteSpace(combinedSnippet))
                    continue;

                yield return new AnswerStreamEvent.CitationReceived(
                    new Citation(
                        DocumentId: "",
                        DocumentName: fileName,
                        PageNumber: evidence.Page,
                        CitedText: combinedSnippet,
                        HighlightAreas: []));
            }

            yield return new AnswerStreamEvent.Done();
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static string BuildPrompt(string filePath, string question)
    {
        // Use forward slashes for cross-platform compatibility in the prompt
        var normalizedPath = filePath.Replace('\\', '/');
        return $$"""
            Read the PDF file at {{normalizedPath}}.

            Answer this question based ONLY on the document content: {{question}}

            LANGUAGE RULE: Answer in the same language as the question.

            Return ONLY a JSON object — no markdown fences, no explanation, no other text:
            {"answer":"your answer","evidence":[{"page":1,"snippets":["exact text from document"]}]}

            Rules:
            1. Only use information explicitly stated in the document
            2. Copy evidence snippets character-for-character from the document — do NOT paraphrase
            3. Each snippet should be one focused sentence or short passage, not a full paragraph
            4. If the document does not contain the answer, return: {"answer":"I could not find an answer to this question in the provided document.","evidence":[]}
            5. Page numbers are 1-indexed
            6. Keep your answer concise — at most 3-4 short paragraphs. Focus on the most important information.
            7. Limit evidence to the 5 most relevant snippets across all pages
            """;
    }

    /// <summary>
    /// Parses the structured JSON answer from the CLI result text.
    /// Handles markdown fences and leading/trailing noise.
    /// </summary>
    internal static CliAnswerResult? ParseStructuredResult(string text)
    {
        var cleaned = StripMarkdownFences(text).Trim();

        // Try to find JSON object boundaries if there's surrounding text
        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        var json = cleaned[start..(end + 1)];

        try
        {
            var result = JsonSerializer.Deserialize<CliAnswerResult>(json, JsonOptions);
            if (result?.Answer is null)
                return null;
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripMarkdownFences(string text)
    {
        // Strip ```json ... ``` or ``` ... ```
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];
        }
        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^3];
        return trimmed.Trim();
    }

    private void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to delete temp file {Path}", path); }
    }
}

/// <summary>Structured result expected from the Claude Code CLI.</summary>
public record CliAnswerResult
{
    [JsonPropertyName("answer")]
    public required string Answer { get; init; }

    [JsonPropertyName("evidence")]
    public List<CliEvidence> Evidence { get; init; } = [];
}

/// <summary>Single evidence entry from CLI output.</summary>
public record CliEvidence
{
    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("snippets")]
    public List<string> Snippets { get; init; } = [];
}
