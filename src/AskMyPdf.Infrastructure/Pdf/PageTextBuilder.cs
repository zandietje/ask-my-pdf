namespace AskMyPdf.Infrastructure.Pdf;

using AskMyPdf.Core.Models;

/// <summary>
/// Reconstructs readable text from word bounding boxes extracted by PdfPig.
/// Groups words into visual lines based on Y-coordinate proximity.
/// </summary>
public static class PageTextBuilder
{
    private const double LineTolerance = 2.0;

    /// <summary>
    /// Reconstructs readable text from the word bounding boxes on a page.
    /// Groups words into visual lines and joins with spaces/newlines.
    /// </summary>
    public static string ReconstructPageText(PageBoundingData page)
    {
        var lines = GroupWordsIntoLines(page.Words);
        return string.Join("\n", lines.Select(line =>
            string.Join(" ", line.Select(w => w.Text))));
    }

    /// <summary>
    /// Groups words into lines based on Y-coordinate proximity.
    /// Words within LineTolerance PDF units of each other are on the same line.
    /// </summary>
    public static List<List<WordBoundingBox>> GroupWordsIntoLines(List<WordBoundingBox> words)
    {
        if (words.Count == 0) return [];

        var lines = new List<List<WordBoundingBox>> { new() { words[0] } };
        for (var i = 1; i < words.Count; i++)
        {
            if (Math.Abs(words[i].Top - lines[^1][^1].Top) <= LineTolerance)
                lines[^1].Add(words[i]);
            else
                lines.Add([words[i]]);
        }
        return lines;
    }
}
