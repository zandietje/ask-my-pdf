namespace AskMyPdf.Infrastructure.Pdf;

using System.Text.RegularExpressions;
using AskMyPdf.Core.Models;

public partial class CoordinateTransformer
{
    private const double LineTolerance = 2.0; // PDF units — words within this Y-distance are on the same line

    public List<HighlightArea> ToHighlightAreas(
        string citedText,
        int pageNumber,
        List<PageBoundingData> pages)
    {
        var page = pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is null || page.Words.Count == 0)
            return [];

        var normalizedCited = Normalize(citedText);
        if (string.IsNullOrEmpty(normalizedCited))
            return [];

        var (startIdx, endIdx) = FindMatchingWords(normalizedCited, page.Words);
        if (startIdx < 0)
            return [];

        var matchedWords = page.Words.GetRange(startIdx, endIdx - startIdx + 1);
        return GroupIntoHighlightAreas(matchedWords, page, pageNumber);
    }

    internal static string Normalize(string text)
    {
        // Strip control characters
        var cleaned = ControlCharsRegex().Replace(text, "");
        // Collapse whitespace to single space
        cleaned = WhitespaceRegex().Replace(cleaned, " ");
        return cleaned.Trim().ToLowerInvariant();
    }

    internal static (int StartIdx, int EndIdx) FindMatchingWords(string normalizedCited, List<WordBoundingBox> words)
    {
        var normalizedWords = words.Select(w => Normalize(w.Text)).ToList();

        // Try each starting position — skip words that can't start the cited text
        for (var start = 0; start < words.Count; start++)
        {
            if (normalizedWords[start].Length == 0)
                continue;

            // The cited text must start with (or within) this word
            if (!normalizedCited.StartsWith(normalizedWords[start])
                && !normalizedWords[start].Contains(normalizedCited[..Math.Min(3, normalizedCited.Length)]))
                continue;

            var concat = normalizedWords[start];

            for (var end = start; end < words.Count; end++)
            {
                if (end > start)
                    concat += " " + normalizedWords[end];

                if (concat.Contains(normalizedCited))
                    return (start, end);

                if (concat.Length > normalizedCited.Length + 50)
                    break;
            }
        }

        return (-1, -1);
    }

    internal static List<HighlightArea> GroupIntoHighlightAreas(
        List<WordBoundingBox> matchedWords,
        PageBoundingData page,
        int pageNumber)
    {
        if (matchedWords.Count == 0)
            return [];

        var pageIndex = pageNumber - 1; // Viewer is 0-indexed
        var areas = new List<HighlightArea>();

        // Group words into lines based on similar Top values
        var currentLine = new List<WordBoundingBox> { matchedWords[0] };

        for (var i = 1; i < matchedWords.Count; i++)
        {
            var word = matchedWords[i];
            var prevWord = currentLine[^1];

            // Same line if Top values are within tolerance
            if (Math.Abs(word.Top - prevWord.Top) <= LineTolerance)
            {
                currentLine.Add(word);
            }
            else
            {
                // Emit current line as a highlight area
                areas.Add(LineToHighlightArea(currentLine, page, pageIndex));
                currentLine = [word];
            }
        }

        // Emit last line
        areas.Add(LineToHighlightArea(currentLine, page, pageIndex));

        return areas;
    }

    private static HighlightArea LineToHighlightArea(
        List<WordBoundingBox> lineWords,
        PageBoundingData page,
        int pageIndex)
    {
        var minLeft = lineWords.Min(w => w.Left);
        var maxRight = lineWords.Max(w => w.Right);
        var minBottom = lineWords.Min(w => w.Bottom);
        var maxTop = lineWords.Max(w => w.Top);

        // PdfPig: origin bottom-left, Y up
        // Viewer: origin top-left, Y down, percentages 0-100
        var leftPct = (minLeft / page.PageWidth) * 100;
        var topPct = ((page.PageHeight - maxTop) / page.PageHeight) * 100;
        var widthPct = ((maxRight - minLeft) / page.PageWidth) * 100;
        var heightPct = ((maxTop - minBottom) / page.PageHeight) * 100;

        return new HighlightArea(pageIndex, leftPct, topPct, widthPct, heightPct);
    }

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex ControlCharsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
