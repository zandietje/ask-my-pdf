namespace AskMyPdf.Infrastructure.Pdf;

using System.Text;
using AskMyPdf.Core.Models;

public class CoordinateTransformer
{
    private const double LineTolerance = 2.0; // PDF units — words within this Y-distance are on the same line

    /// <summary>
    /// Finds highlight areas for the cited text on the given page.
    /// Uses character-level dense matching to handle tokenization differences.
    /// </summary>
    public List<HighlightArea> ToHighlightAreas(
        string citedText,
        int pageNumber,
        List<PageBoundingData> pages)
    {
        var page = pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is null || page.Words.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(citedText))
            return [];

        var matchedIndices = FindMatchedWordIndices(citedText, page.Words);
        if (matchedIndices.Count == 0)
            return [];

        var matchedWords = matchedIndices.Select(i => page.Words[i]).ToList();
        return GroupIntoHighlightAreas(matchedWords, page, pageNumber);
    }

    /// <summary>
    /// Matches cited text against page words. Tries dense substring matching first,
    /// falls back to per-line, then per-word matching for two-column PDFs where
    /// words from different columns are interleaved in PdfPig's word order.
    /// </summary>
    internal static List<int> FindMatchedWordIndices(string citedText, List<WordBoundingBox> words)
    {
        // Build dense page string + char-to-word-index map
        var pageBuilder = new StringBuilder();
        var charToWord = new List<int>();

        for (var i = 0; i < words.Count; i++)
        {
            foreach (var ch in words[i].Text)
            {
                if (!char.IsWhiteSpace(ch) && !char.IsControl(ch))
                {
                    pageBuilder.Append(char.ToLowerInvariant(ch));
                    charToWord.Add(i);
                }
            }
        }

        var densePageStr = pageBuilder.ToString();

        // Try full cited text as one contiguous match (best case)
        var fullTarget = ToDense(citedText);
        var matchedIndices = FindAllDenseOccurrences(fullTarget, densePageStr, charToWord);
        if (matchedIndices.Count > 0)
            return matchedIndices;

        // Per-line: try dense match, fall back to per-word for lines that fail
        // (two-column PDFs interleave words from both columns in PdfPig order,
        //  so right-column text like "ReactJS, React Native" isn't contiguous)
        var lines = citedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var allMatched = new HashSet<int>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var lineMatches = FindAllDenseOccurrences(ToDense(trimmed), densePageStr, charToWord);
            if (lineMatches.Count > 0)
            {
                foreach (var idx in lineMatches)
                    allMatched.Add(idx);
            }
            else
            {
                foreach (var idx in MatchByWordText(trimmed, words))
                    allMatched.Add(idx);
            }
        }

        return allMatched.Count > 0 ? allMatched.Order().ToList() : [];
    }

    private static List<int> FindAllDenseOccurrences(
        string target, string densePageStr, List<int> charToWord)
    {
        if (target.Length == 0 || target.Length > densePageStr.Length)
            return [];

        var matched = new HashSet<int>();
        var pos = 0;
        while (pos <= densePageStr.Length - target.Length)
        {
            var found = densePageStr.IndexOf(target, pos, StringComparison.Ordinal);
            if (found < 0) break;

            for (var i = found; i < found + target.Length; i++)
                matched.Add(charToWord[i]);

            pos = found + target.Length;
        }

        return matched.Count > 0 ? matched.Order().ToList() : [];
    }

    private static List<int> MatchByWordText(string citedText, List<WordBoundingBox> words)
    {
        var tokens = new HashSet<string>();
        foreach (var raw in citedText.Split(
            [' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = NormalizeToken(raw);
            if (clean.Length >= 4)
                tokens.Add(clean);
        }

        if (tokens.Count == 0)
            return [];

        var matched = new List<int>();
        for (var i = 0; i < words.Count; i++)
        {
            if (tokens.Contains(NormalizeToken(words[i].Text)))
                matched.Add(i);
        }

        return matched;
    }

    private static string NormalizeToken(string word) =>
        word.ToLowerInvariant().Trim(',', ':', ';', '(', ')', '"', '\'', '*');

    internal static string ToDense(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch) && !char.IsControl(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    internal static List<List<WordBoundingBox>> GroupWordsIntoLines(List<WordBoundingBox> words)
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

    internal static List<HighlightArea> GroupIntoHighlightAreas(
        List<WordBoundingBox> matchedWords,
        PageBoundingData page,
        int pageNumber)
    {
        if (matchedWords.Count == 0)
            return [];

        var pageIndex = pageNumber - 1; // Viewer is 0-indexed
        return GroupWordsIntoLines(matchedWords)
            .Select(line => LineToHighlightArea(line, page, pageIndex))
            .ToList();
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

        // PdfPig: origin bottom-left, Y up → Viewer: origin top-left, Y down, percentages 0-100
        var leftPct = (minLeft / page.PageWidth) * 100;
        var topPct = ((page.PageHeight - maxTop) / page.PageHeight) * 100;
        var widthPct = ((maxRight - minLeft) / page.PageWidth) * 100;
        var heightPct = ((maxTop - minBottom) / page.PageHeight) * 100;

        return new HighlightArea(pageIndex, leftPct, topPct, widthPct, heightPct);
    }
}
