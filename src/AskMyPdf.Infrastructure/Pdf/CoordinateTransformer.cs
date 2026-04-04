namespace AskMyPdf.Infrastructure.Pdf;

using System.Globalization;
using System.Text;
using AskMyPdf.Core.Models;

public class CoordinateTransformer
{
    private const double LineTolerance = 2.0; // PDF units — words within this Y-distance are on the same line

    /// <summary>
    /// Finds highlight areas for the cited text on the given page.
    /// Uses character-level dense matching to handle tokenization differences.
    /// </summary>
    /// <param name="contiguousOnly">
    /// When true, uses spatial ordering and contiguous matching with a bounded
    /// per-word fallback. Use for engines that return exact document snippets
    /// (e.g. CLI engine) to highlight only the target passage.
    /// </param>
    public List<HighlightArea> ToHighlightAreas(
        string citedText,
        int pageNumber,
        List<PageBoundingData> pages,
        bool contiguousOnly = false)
    {
        var page = pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is null || page.Words.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(citedText))
            return [];

        var matchedIndices = contiguousOnly
            ? FindContiguousMatch(citedText, page.Words)
            : FindMatchedWordIndices(citedText, page.Words);

        if (matchedIndices.Count == 0)
            return [];

        var matchedWords = matchedIndices.Select(i => page.Words[i]).ToList();
        return GroupIntoHighlightAreas(matchedWords, page, pageNumber);
    }

    /// <summary>
    /// Spatially-aware matching for exact snippets (CLI engine).
    /// 1. Reorders words spatially (by visual line, left-to-right) for two-column support.
    /// 2. Tries contiguous dense matching with Unicode normalization.
    /// 3. Falls back to per-word matching bounded to the tightest spatial region.
    /// </summary>
    internal static List<int> FindContiguousMatch(string citedText, List<WordBoundingBox> words)
    {
        // Build spatially-ordered word list: group by Y (line), sort by X within each line.
        var ordered = BuildSpatialOrder(words);

        // Build dense string from spatially-ordered words (with diacritics stripped)
        var pageBuilder = new StringBuilder();
        var charToOriginal = new List<int>();

        foreach (var (word, originalIndex) in ordered)
        {
            foreach (var ch in NormalizeChar(word.Text))
            {
                pageBuilder.Append(ch);
                charToOriginal.Add(originalIndex);
            }
        }

        var densePageStr = pageBuilder.ToString();
        var target = ToDenseNormalized(citedText);

        // Strategy 1: full contiguous match
        var matchedIndices = FindFirstDenseOccurrence(target, densePageStr, charToOriginal);
        if (matchedIndices.Count > 0)
            return matchedIndices;

        // Strategy 2: per-line contiguous match (multi-line snippets)
        var snippetLines = citedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (snippetLines.Length > 1)
        {
            var allMatched = new HashSet<int>();
            foreach (var line in snippetLines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                var lineMatches = FindFirstDenseOccurrence(
                    ToDenseNormalized(trimmed), densePageStr, charToOriginal);
                foreach (var idx in lineMatches)
                    allMatched.Add(idx);
            }
            if (allMatched.Count > 0)
                return allMatched.Order().ToList();
        }

        // Strategy 3: per-word matching, bounded to the tightest spatial cluster.
        // Matches individual words but keeps only the smallest contiguous run of
        // word indices that contains a majority of matches — avoids scattered highlights.
        return FindBoundedWordMatch(citedText, words, ordered);
    }

    /// <summary>
    /// Per-word matching with spatial bounding: matches individual tokens, then finds
    /// the shortest contiguous span of word indices containing most matches.
    /// This avoids scattered highlights while still working when dense matching fails
    /// due to character-level differences between text extraction engines.
    /// </summary>
    private static List<int> FindBoundedWordMatch(
        string citedText,
        List<WordBoundingBox> words,
        List<(WordBoundingBox Word, int OriginalIndex)> spatialOrder)
    {
        // Collect tokens from the snippet (4+ chars, normalized)
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

        // Find which words in spatial order match any token
        var matchFlags = new bool[spatialOrder.Count];
        var matchCount = 0;
        for (var i = 0; i < spatialOrder.Count; i++)
        {
            if (tokens.Contains(NormalizeToken(spatialOrder[i].Word.Text)))
            {
                matchFlags[i] = true;
                matchCount++;
            }
        }
        if (matchCount == 0)
            return [];

        // Find the shortest window in spatial order that contains ≥ 70% of matched words.
        // This concentrates the highlight on the actual passage.
        var threshold = Math.Max(1, (int)(matchCount * 0.7));
        var bestStart = 0;
        var bestLen = spatialOrder.Count;
        var windowMatches = 0;
        var left = 0;

        for (var right = 0; right < spatialOrder.Count; right++)
        {
            if (matchFlags[right]) windowMatches++;

            while (windowMatches >= threshold)
            {
                var windowLen = right - left + 1;
                if (windowLen < bestLen)
                {
                    bestStart = left;
                    bestLen = windowLen;
                }
                if (matchFlags[left]) windowMatches--;
                left++;
            }
        }

        // Return original indices of matched words within the best window only
        var result = new List<int>();
        for (var i = bestStart; i < bestStart + bestLen && i < spatialOrder.Count; i++)
        {
            if (matchFlags[i])
                result.Add(spatialOrder[i].OriginalIndex);
        }

        return result.Count > 0 ? result.Order().ToList() : [];
    }

    /// <summary>
    /// Builds a spatially-ordered word list: groups by visual line (Y), sorts left-to-right (X).
    /// </summary>
    private static List<(WordBoundingBox Word, int OriginalIndex)> BuildSpatialOrder(List<WordBoundingBox> words)
    {
        var sorted = words
            .Select((w, i) => (Word: w, OriginalIndex: i))
            .OrderBy(x => x.Word.Top)
            .ToList();

        var lines = new List<List<(WordBoundingBox Word, int OriginalIndex)>> { new() { sorted[0] } };
        for (var i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i].Word.Top - lines[^1][^1].Word.Top) <= LineTolerance)
                lines[^1].Add(sorted[i]);
            else
                lines.Add([sorted[i]]);
        }

        return lines
            .SelectMany(line => line.OrderBy(x => x.Word.Left))
            .ToList();
    }

    /// <summary>
    /// Finds the first dense occurrence of target in the page string.
    /// Returns the original word indices that were matched.
    /// </summary>
    private static List<int> FindFirstDenseOccurrence(
        string target, string densePageStr, List<int> charToWord)
    {
        if (target.Length == 0 || target.Length > densePageStr.Length)
            return [];

        var found = densePageStr.IndexOf(target, StringComparison.Ordinal);
        if (found < 0)
            return [];

        var matched = new HashSet<int>();
        for (var i = found; i < found + target.Length; i++)
            matched.Add(charToWord[i]);

        return matched.Order().ToList();
    }

    /// <summary>
    /// Matches cited text against page words. Tries dense substring matching first,
    /// falls back to per-line, then per-word matching for two-column PDFs where
    /// words from different columns are interleaved in PdfPig's word order.
    /// </summary>
    internal static List<int> FindMatchedWordIndices(
        string citedText, List<WordBoundingBox> words, bool contiguousOnly = false)
    {
        if (contiguousOnly)
            return FindContiguousMatch(citedText, words);

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
        StripDiacritics(word.ToLowerInvariant()).Trim(',', '.', ':', ';', '(', ')', '"', '\'', '*');

    /// <summary>
    /// Dense string for the original matching path (Anthropic engine).
    /// Strips whitespace/control chars, lowercases. No diacritic stripping.
    /// </summary>
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

    /// <summary>
    /// Dense string with Unicode normalization for cross-engine matching (CLI engine).
    /// Strips diacritics so "ë" (precomposed) matches "e" + combining diaeresis, etc.
    /// </summary>
    internal static string ToDenseNormalized(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in NormalizeChar(text))
        {
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Yields normalized characters: lowercase, whitespace/control stripped, diacritics removed.
    /// </summary>
    private static IEnumerable<char> NormalizeChar(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        foreach (var ch in decomposed)
        {
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // strip combining diacritics
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                continue;
            yield return char.ToLowerInvariant(ch);
        }
    }

    /// <summary>Strips diacritics from text (e.g. ë → e, é → e).</summary>
    private static string StripDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (char.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
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
