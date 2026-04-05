namespace AskMyPdf.Infrastructure.Pdf;

using System.Text;
using AskMyPdf.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

/// <summary>
/// Extracts words from PDF pages using PdfPig's Document Layout Analysis pipeline.
/// Produces a canonical per-page representation with correct reading order,
/// so that citation matching can use simple substring search at query time.
/// </summary>
public class BoundingBoxExtractor
{
    private const double LineTolerance = 2.0; // PDF units — words within this Y-distance are on the same line

    public List<PageCanonicalData> ExtractPages(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var pages = new List<PageCanonicalData>(document.NumberOfPages);

        for (var i = 1; i <= document.NumberOfPages; i++)
        {
            var page = document.GetPage(i);
            pages.Add(ExtractPage(page, i));
        }

        return pages;
    }

    private PageCanonicalData ExtractPage(Page page, int pageNumber)
    {
        // Step 1: Extract words using proximity-based grouping (better than default)
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance);
        if (!words.Any())
            return new PageCanonicalData(pageNumber, page.Width, page.Height, "", []);

        // Step 2: Segment page into text blocks (detects columns, paragraphs)
        var blocks = RecursiveXYCut.Instance.GetBlocks(words);
        if (blocks.Count == 0)
        {
            // Fallback: treat all words as a single block in default spatial order
            return BuildFromWords(words.ToList(), page, pageNumber);
        }

        // Step 3: Order blocks into reading sequence
        var orderedBlocks = UnsupervisedReadingOrderDetector.Instance.Get(blocks);

        // Step 4: Build canonical text + tokens from ordered blocks
        var textBuilder = new StringBuilder();
        var tokens = new List<PageToken>();

        foreach (var block in orderedBlocks)
        {
            // Get words belonging to this block, grouped into lines
            var blockWords = block.TextLines
                .SelectMany(line => line.Words)
                .ToList();

            if (blockWords.Count == 0) continue;

            // Separate blocks with newline
            if (textBuilder.Length > 0)
                textBuilder.Append('\n');

            AppendWordsAsLines(blockWords, textBuilder, tokens);
        }

        return new PageCanonicalData(
            pageNumber, page.Width, page.Height,
            textBuilder.ToString(), tokens);
    }

    /// <summary>
    /// Fallback: build canonical data from a flat word list (no block structure).
    /// Groups words into lines by Y proximity, sorts left-to-right within each line.
    /// </summary>
    private static PageCanonicalData BuildFromWords(
        List<Word> words, Page page, int pageNumber)
    {
        var textBuilder = new StringBuilder();
        var tokens = new List<PageToken>();
        AppendWordsAsLines(words, textBuilder, tokens);
        return new PageCanonicalData(
            pageNumber, page.Width, page.Height,
            textBuilder.ToString(), tokens);
    }

    /// <summary>
    /// Groups words into visual lines by Y proximity, sorts left-to-right within each line,
    /// and appends them to the text builder with proper char offsets.
    /// </summary>
    private static void AppendWordsAsLines(
        List<Word> words, StringBuilder textBuilder, List<PageToken> tokens)
    {
        // Group into visual lines by Y coordinate
        var sorted = words
            .OrderByDescending(w => w.BoundingBox.Top)  // Top of page first
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<List<Word>> { new() { sorted[0] } };
        for (var i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i].BoundingBox.Top - lines[^1][^1].BoundingBox.Top) <= LineTolerance)
                lines[^1].Add(sorted[i]);
            else
                lines.Add([sorted[i]]);
        }

        // Build text + tokens line by line
        for (var lineIdx = 0; lineIdx < lines.Count; lineIdx++)
        {
            if (lineIdx > 0)
                textBuilder.Append('\n');

            var line = lines[lineIdx].OrderBy(w => w.BoundingBox.Left).ToList();

            for (var wordIdx = 0; wordIdx < line.Count; wordIdx++)
            {
                if (wordIdx > 0)
                    textBuilder.Append(' ');

                var w = line[wordIdx];
                tokens.Add(new PageToken(
                    w.Text,
                    textBuilder.Length,
                    w.BoundingBox.Left,
                    w.BoundingBox.Bottom,
                    w.BoundingBox.Right,
                    w.BoundingBox.Top));

                textBuilder.Append(w.Text);
            }
        }
    }
}
