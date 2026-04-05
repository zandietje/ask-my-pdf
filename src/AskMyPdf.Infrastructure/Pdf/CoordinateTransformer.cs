namespace AskMyPdf.Infrastructure.Pdf;

using System.Globalization;
using System.Text;
using AskMyPdf.Core.Models;

/// <summary>
/// Matches cited text against the stored canonical page representation and
/// converts matched token bounding boxes into viewer-ready highlight areas.
///
/// Because reading order is solved at ingestion time (via the DLA pipeline in
/// BoundingBoxExtractor), matching is a simple normalized substring search —
/// no spatial reordering or heuristic fallbacks needed at query time.
/// </summary>
public class CoordinateTransformer
{
    private const double LineTolerance = 2.0; // PDF units — tokens within this Y-distance are on the same line

    /// <summary>
    /// Finds highlight areas for the cited text on the given page.
    /// Searches against the stored canonical text using normalized substring matching.
    /// </summary>
    public List<HighlightArea> ToHighlightAreas(
        string citedText,
        int pageNumber,
        List<PageCanonicalData> pages)
    {
        var page = pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is null || page.Tokens.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(citedText))
            return [];

        // Build dense normalized string from canonical text with char→token mapping
        var (denseText, charToToken) = BuildDenseMapping(page);
        var target = ToDenseNormalized(citedText);

        // Strategy 1: full substring match
        var matchedTokenIndices = FindDenseMatch(target, denseText, charToToken);

        // Strategy 2: per-line match (for multi-line citations containing \n)
        if (matchedTokenIndices.Count == 0)
        {
            var lines = citedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 1)
            {
                var all = new HashSet<int>();
                foreach (var line in lines)
                {
                    var lineDense = ToDenseNormalized(line.Trim());
                    if (lineDense.Length == 0) continue;
                    foreach (var idx in FindDenseMatch(lineDense, denseText, charToToken))
                        all.Add(idx);
                }
                matchedTokenIndices = all.Order().ToList();
            }
        }

        // Strategy 3: individual word matching (bag of words) — last resort when
        // substring/line matching fails (e.g. LLM reworded or reordered the citation)
        if (matchedTokenIndices.Count == 0)
        {
            matchedTokenIndices = FindIndividualWordMatches(citedText, page);
        }

        if (matchedTokenIndices.Count == 0)
            return [];

        var matchedTokens = matchedTokenIndices.Select(i => page.Tokens[i]).ToList();
        return GroupIntoHighlightAreas(matchedTokens, page, pageNumber);
    }

    /// <summary>
    /// Builds a dense normalized string from the canonical text and a mapping
    /// from each dense char position to the token index it belongs to.
    /// </summary>
    private static (string DenseText, List<int> CharToToken) BuildDenseMapping(PageCanonicalData page)
    {
        var denseBuilder = new StringBuilder(page.CanonicalText.Length);
        var charToToken = new List<int>(page.CanonicalText.Length);

        // Build sorted token offset→index lookup for mapping chars to tokens
        // Tokens are in reading order; we walk canonical text and assign each char
        // to the token whose range [Offset, Offset+Text.Length) contains it.
        var tokenIndex = 0;
        var nextTokenStart = page.Tokens.Count > 1 ? page.Tokens[1].Offset : int.MaxValue;

        for (var i = 0; i < page.CanonicalText.Length; i++)
        {
            // Advance to the correct token for this char position
            while (tokenIndex < page.Tokens.Count - 1 && i >= nextTokenStart)
            {
                tokenIndex++;
                nextTokenStart = tokenIndex < page.Tokens.Count - 1
                    ? page.Tokens[tokenIndex + 1].Offset
                    : int.MaxValue;
            }

            var ch = page.CanonicalText[i];

            // Normalize: decompose, strip diacritics, skip whitespace/control, lowercase
            foreach (var normalized in NormalizeChar(ch))
            {
                // Only map chars that fall within a token's text range
                var token = page.Tokens[tokenIndex];
                if (i >= token.Offset && i < token.Offset + token.Text.Length)
                {
                    denseBuilder.Append(normalized);
                    charToToken.Add(tokenIndex);
                }
            }
        }

        return (denseBuilder.ToString(), charToToken);
    }

    /// <summary>
    /// Finds the first occurrence of target in denseText and returns the
    /// distinct token indices that the matched chars map to.
    /// </summary>
    private static List<int> FindDenseMatch(
        string target, string denseText, List<int> charToToken)
    {
        if (target.Length == 0 || target.Length > denseText.Length)
            return [];

        var pos = denseText.IndexOf(target, StringComparison.Ordinal);
        if (pos < 0)
            return [];

        var matched = new HashSet<int>();
        for (var i = pos; i < pos + target.Length && i < charToToken.Count; i++)
            matched.Add(charToToken[i]);

        return matched.Order().ToList();
    }

    /// <summary>
    /// Fallback: splits the cited text into individual words, normalizes each, and matches
    /// them independently against page tokens. Finds the first occurrence of each word.
    /// Skips very short words (&lt;3 chars after normalization) to avoid false positives
    /// from articles and prepositions.
    /// </summary>
    internal static List<int> FindIndividualWordMatches(string citedText, PageCanonicalData page)
    {
        var words = citedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var normalizedWords = words
            .Select(w => ToDenseNormalized(w))
            .Where(w => w.Length >= 3)
            .Distinct()
            .ToHashSet();

        if (normalizedWords.Count == 0)
            return [];

        // Normalize each page token once
        var matched = new HashSet<int>();
        var found = new HashSet<string>();

        for (var i = 0; i < page.Tokens.Count; i++)
        {
            var tokenNorm = ToDenseNormalized(page.Tokens[i].Text);
            if (tokenNorm.Length > 0 && normalizedWords.Contains(tokenNorm) && found.Add(tokenNorm))
            {
                matched.Add(i);
            }
        }

        return matched.Order().ToList();
    }

    /// <summary>
    /// Normalizes a string to a dense form: lowercase, diacritics stripped, whitespace removed.
    /// </summary>
    internal static string ToDenseNormalized(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            foreach (var normalized in NormalizeChar(ch))
                sb.Append(normalized);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Yields normalized characters: lowercase, whitespace/control stripped, diacritics removed,
    /// compatibility ligatures decomposed (ﬁ→fi, ﬂ→fl via NFKD).
    /// </summary>
    private static IEnumerable<char> NormalizeChar(char ch)
    {
        if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            yield break;

        var decomposed = ch.ToString().Normalize(NormalizationForm.FormKD);
        foreach (var d in decomposed)
        {
            if (char.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark)
                continue; // strip combining diacritics
            yield return char.ToLowerInvariant(d);
        }
    }

    /// <summary>
    /// Groups matched tokens into visual lines and converts each line
    /// to a viewer-ready highlight area with percentage coordinates.
    /// </summary>
    internal static List<HighlightArea> GroupIntoHighlightAreas(
        List<PageToken> matchedTokens,
        PageCanonicalData page,
        int pageNumber)
    {
        if (matchedTokens.Count == 0)
            return [];

        var pageIndex = pageNumber - 1; // Viewer is 0-indexed
        return GroupTokensIntoLines(matchedTokens)
            .Select(line => LineToHighlightArea(line, page, pageIndex))
            .ToList();
    }

    /// <summary>Groups tokens by Y-coordinate proximity into visual lines.</summary>
    internal static List<List<PageToken>> GroupTokensIntoLines(List<PageToken> tokens)
    {
        if (tokens.Count == 0) return [];

        var sorted = tokens.OrderByDescending(t => t.Top).ThenBy(t => t.Left).ToList();
        var lines = new List<List<PageToken>> { new() { sorted[0] } };

        for (var i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i].Top - lines[^1][^1].Top) <= LineTolerance)
                lines[^1].Add(sorted[i]);
            else
                lines.Add([sorted[i]]);
        }

        return lines;
    }

    private static HighlightArea LineToHighlightArea(
        List<PageToken> lineTokens,
        PageCanonicalData page,
        int pageIndex)
    {
        var minLeft = lineTokens.Min(t => t.Left);
        var maxRight = lineTokens.Max(t => t.Right);
        var minBottom = lineTokens.Min(t => t.Bottom);
        var maxTop = lineTokens.Max(t => t.Top);

        // PdfPig: origin bottom-left, Y up → Viewer: origin top-left, Y down, percentages 0-100
        var leftPct = (minLeft / page.PageWidth) * 100;
        var topPct = ((page.PageHeight - maxTop) / page.PageHeight) * 100;
        var widthPct = ((maxRight - minLeft) / page.PageWidth) * 100;
        var heightPct = ((maxTop - minBottom) / page.PageHeight) * 100;

        return new HighlightArea(pageIndex, leftPct, topPct, widthPct, heightPct);
    }
}
