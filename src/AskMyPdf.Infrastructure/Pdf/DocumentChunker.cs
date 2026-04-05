namespace AskMyPdf.Infrastructure.Pdf;

using System.Text;
using AskMyPdf.Core.Models;

public class DocumentChunker
{
    private const int TargetChunkSize = 150;   // characters — small so each chunk ≈ 1-2 sentences for precise highlighting
    private const int OverlapSize = 50;         // characters of overlap between chunks

    public List<DocumentChunk> ChunkDocument(string documentId, List<PageBoundingData> pages)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        foreach (var page in pages)
        {
            var pageText = PageTextBuilder.ReconstructPageText(page);
            if (string.IsNullOrWhiteSpace(pageText))
                continue;

            var pageChunks = SplitIntoChunks(pageText);
            foreach (var chunkText in pageChunks)
            {
                chunks.Add(new DocumentChunk(
                    documentId, page.PageNumber, chunkIndex++, chunkText));
            }
        }

        return chunks;
    }

    private static List<string> SplitIntoChunks(string text)
    {
        if (text.Length <= TargetChunkSize)
            return [text];

        var chunks = new List<string>();
        var sentences = SplitSentences(text);
        var current = new StringBuilder();

        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];

            if (current.Length > 0 && current.Length + sentence.Length > TargetChunkSize)
            {
                chunks.Add(current.ToString().Trim());

                // Start new chunk with overlap: walk backwards to include ~OverlapSize chars
                current.Clear();
                var overlapChars = 0;
                for (var j = i - 1; j >= 0 && overlapChars < OverlapSize; j--)
                {
                    current.Insert(0, sentences[j]);
                    overlapChars += sentences[j].Length;
                }
            }

            current.Append(sentence);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks;
    }

    private static List<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            // Always split on newlines (each visual line is a chunk boundary)
            if (text[i] == '\n')
            {
                var segment = text[start..(i + 1)].Trim();
                if (segment.Length > 0)
                    sentences.Add(segment + " ");
                start = i + 1;
                continue;
            }

            // Split on sentence-ending punctuation followed by whitespace or end
            if (text[i] is '.' or '!' or '?')
            {
                if (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))
                {
                    sentences.Add(text[start..(i + 1)]);
                    var next = i + 1;
                    while (next < text.Length && text[next] is ' ' or '\t')
                        next++;
                    start = next;
                    i = next - 1;
                }
            }
        }

        if (start < text.Length)
            sentences.Add(text[start..]);

        return sentences;
    }
}
