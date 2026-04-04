using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class CoordinateTransformerTests
{
    private readonly CoordinateTransformer _transformer = new();

    private static PageBoundingData CreatePage(int pageNumber, double width, double height, List<WordBoundingBox> words)
        => new(pageNumber, width, height, words);

    // --- Coordinate conversion tests ---

    [Fact]
    public void ToHighlightAreas_Known_Coordinates_Produce_Correct_Percentages()
    {
        var words = new List<WordBoundingBox> { new("Hello", 10, 150, 30, 170) };
        var pages = new List<PageBoundingData> { CreatePage(1, 100, 200, words) };

        var areas = _transformer.ToHighlightAreas("Hello", 1, pages);

        areas.Should().HaveCount(1);
        var area = areas[0];
        area.Left.Should().BeApproximately(10.0, 0.01);
        area.Top.Should().BeApproximately(15.0, 0.01);
        area.Width.Should().BeApproximately(20.0, 0.01);
        area.Height.Should().BeApproximately(10.0, 0.01);
    }

    [Fact]
    public void ToHighlightAreas_YAxis_Is_Flipped()
    {
        var words = new List<WordBoundingBox> { new("Top", 0, 190, 50, 200) };
        var pages = new List<PageBoundingData> { CreatePage(1, 100, 200, words) };

        var areas = _transformer.ToHighlightAreas("Top", 1, pages);

        areas.Should().HaveCount(1);
        areas[0].Top.Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void ToHighlightAreas_PageIndex_Is_ZeroBased()
    {
        var words = new List<WordBoundingBox> { new("Hello", 10, 100, 50, 112) };
        var pages = new List<PageBoundingData>
        {
            CreatePage(1, 200, 200, []),
            CreatePage(2, 200, 200, words)
        };

        var areas = _transformer.ToHighlightAreas("Hello", 2, pages);

        areas.Should().HaveCount(1);
        areas[0].PageIndex.Should().Be(1);
    }

    // --- Text matching tests ---

    [Fact]
    public void ToHighlightAreas_TextMatching_Finds_Correct_Words()
    {
        var words = new List<WordBoundingBox>
        {
            new("The", 10, 100, 30, 112),
            new("quick", 35, 100, 60, 112),
            new("brown", 65, 100, 90, 112),
            new("fox", 95, 100, 110, 112)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        var areas = _transformer.ToHighlightAreas("quick brown", 1, pages);

        areas.Should().HaveCount(1);
        areas[0].Left.Should().BeApproximately(17.5, 0.01);
        areas[0].Width.Should().BeApproximately(27.5, 0.01);
    }

    [Fact]
    public void ToHighlightAreas_CaseInsensitive()
    {
        var words = new List<WordBoundingBox>
        {
            new("Hello", 10, 100, 50, 112),
            new("WORLD", 55, 100, 95, 112)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        _transformer.ToHighlightAreas("hello world", 1, pages).Should().HaveCount(1);
    }

    [Fact]
    public void ToHighlightAreas_Whitespace_Normalized()
    {
        var words = new List<WordBoundingBox>
        {
            new("Hello", 10, 100, 50, 112),
            new("World", 55, 100, 95, 112)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        _transformer.ToHighlightAreas("  Hello   World  ", 1, pages).Should().HaveCount(1);
    }

    [Fact]
    public void ToHighlightAreas_Newlines_In_CitedText()
    {
        var words = new List<WordBoundingBox>
        {
            new("Ltd.", 10, 100, 40, 112),
            new("65", 45, 100, 55, 112),
            new("Chulia", 60, 100, 90, 112),
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        _transformer.ToHighlightAreas("Ltd.\n65 Chulia", 1, pages).Should().NotBeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_Tokenization_Differences()
    {
        // PdfPig splits "Q&A" into separate tokens
        var words = new List<WordBoundingBox>
        {
            new("Document", 10, 100, 60, 112),
            new("Q", 65, 100, 72, 112),
            new("&", 73, 100, 78, 112),
            new("A", 79, 100, 86, 112),
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        _transformer.ToHighlightAreas("Document Q&A", 1, pages).Should().NotBeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_MultiLine_Produces_Multiple_Areas()
    {
        var words = new List<WordBoundingBox>
        {
            new("First", 10, 100, 50, 112),
            new("line", 55, 100, 80, 112),
            new("second", 10, 78, 60, 90),
            new("line", 65, 78, 85, 90)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        _transformer.ToHighlightAreas("First line second line", 1, pages)
            .Should().HaveCount(2);
    }

    [Fact]
    public void ToHighlightAreas_NoMatch_Returns_Empty()
    {
        var words = new List<WordBoundingBox> { new("Hello", 10, 100, 50, 112) };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        _transformer.ToHighlightAreas("Nonexistent", 1, pages).Should().BeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_EmptyPage_Returns_Empty()
    {
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, []) };
        _transformer.ToHighlightAreas("anything", 1, pages).Should().BeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_WrongPage_Returns_Empty()
    {
        var words = new List<WordBoundingBox> { new("Hello", 10, 100, 50, 112) };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        _transformer.ToHighlightAreas("Hello", 5, pages).Should().BeEmpty();
    }

    // --- Per-line fallback tests (two-column PDFs) ---

    [Fact]
    public void FindMatchedWordIndices_PerLine_Fallback_Matches_NonContiguous_Lines()
    {
        // Simulates a two-column PDF where PdfPig interleaves left and right column words.
        // The cited text "Alpha Beta" appears on one visual line, but other words sit between them
        // in PdfPig's reading order. The per-line dense match should still find them.
        var words = new List<WordBoundingBox>
        {
            new("Alpha", 10, 100, 50, 112),
            new("Unrelated", 200, 100, 260, 112),  // right column word interleaved
            new("Beta", 55, 100, 80, 112),
        };

        // Full dense match fails ("alphabeta" not contiguous in page string "alphaunrelatedbeta").
        // Per-line fallback: "Alpha Beta" → dense "alphabeta" still not contiguous,
        // so word-level fallback matches "Alpha" and "Beta" individually (both >= 4 chars).
        var indices = CoordinateTransformer.FindMatchedWordIndices("Alpha Beta", words);

        indices.Should().Contain(0, "should match 'Alpha'");
        indices.Should().Contain(2, "should match 'Beta'");
    }

    [Fact]
    public void FindMatchedWordIndices_MultiLine_CitedText_Matches_Each_Line()
    {
        var words = new List<WordBoundingBox>
        {
            new("First", 10, 100, 50, 112),
            new("line", 55, 100, 80, 112),
            new("Second", 10, 78, 60, 90),
            new("line", 65, 78, 85, 90),
        };

        var indices = CoordinateTransformer.FindMatchedWordIndices("First line\nSecond line", words);

        indices.Should().HaveCount(4, "all words from both lines should match");
    }

    // --- ContiguousOnly / FindContiguousMatch tests ---

    [Fact]
    public void FindContiguousMatch_TwoColumnPdf_SpatialOrderFindsMatch()
    {
        // Simulates a two-column PDF: PdfPig interleaves words from both columns,
        // but "Alpha Beta" are adjacent spatially (sorted by X: Alpha=10, Beta=55, Unrelated=200).
        // Spatial ordering makes them contiguous → dense match succeeds.
        var words = new List<WordBoundingBox>
        {
            new("Alpha", 10, 100, 50, 112),
            new("Unrelated", 200, 100, 260, 112),  // right column word interleaved in PdfPig order
            new("Beta", 55, 100, 80, 112),
        };

        var indices = CoordinateTransformer.FindContiguousMatch("Alpha Beta", words);

        indices.Should().HaveCount(2);
        indices.Should().Contain(0, "should match 'Alpha'");
        indices.Should().Contain(2, "should match 'Beta'");
        indices.Should().NotContain(1, "should NOT match 'Unrelated'");
    }

    [Fact]
    public void FindContiguousMatch_SimpleContiguous_Works()
    {
        var words = new List<WordBoundingBox>
        {
            new("Hello", 10, 100, 50, 112),
            new("World", 55, 100, 90, 112),
        };

        var indices = CoordinateTransformer.FindContiguousMatch("Hello World", words);
        indices.Should().HaveCount(2);
        indices.Should().Contain(0);
        indices.Should().Contain(1);
    }

    [Fact]
    public void FindContiguousMatch_NoMatch_ReturnsEmpty()
    {
        var words = new List<WordBoundingBox>
        {
            new("Hello", 10, 100, 50, 112),
            new("World", 55, 100, 90, 112),
        };

        var indices = CoordinateTransformer.FindContiguousMatch("Goodbye Moon", words);
        indices.Should().BeEmpty();
    }

    [Fact]
    public void FindContiguousMatch_DoesNotMatchScatteredWords()
    {
        // "erfgoed" and "voor" appear in multiple places on the page.
        // Contiguous match for a sentence containing those words should only
        // match the exact contiguous passage, not scattered occurrences.
        var words = new List<WordBoundingBox>
        {
            // Line 1 left column
            new("voor", 10, 200, 35, 212),
            new("erfgoed", 40, 200, 80, 212),
            new("bescherming", 85, 200, 140, 212),
            // Line 1 right column
            new("Het", 300, 200, 320, 212),
            new("erfgoed", 325, 200, 365, 212),
            new("voor", 370, 200, 395, 212),
            new("iedereen", 400, 200, 450, 212),
        };

        var indices = CoordinateTransformer.FindContiguousMatch("Het erfgoed voor iedereen", words);

        // Should match only the right column words (indices 3,4,5,6), not left column
        indices.Should().HaveCount(4);
        indices.Should().Contain(3);
        indices.Should().Contain(4);
        indices.Should().Contain(5);
        indices.Should().Contain(6);
        indices.Should().NotContain(0, "should not match left column 'voor'");
        indices.Should().NotContain(1, "should not match left column 'erfgoed'");
    }

    [Fact]
    public void FindContiguousMatch_DiacriticDifferences_StillMatches()
    {
        // PdfPig extracts "ë" as precomposed (U+00EB), CLI extracts "e" without diacritic.
        // Unicode normalization (FormD + strip combining marks) makes them match.
        var words = new List<WordBoundingBox>
        {
            new("premi\u00ebre", 10, 100, 70, 112),  // "première" with ë
            new("keer", 75, 100, 100, 112),
        };

        // Snippet without the accent
        var indices = CoordinateTransformer.FindContiguousMatch("premiere keer", words);
        indices.Should().HaveCount(2, "diacritic differences should not prevent matching");
    }

    [Fact]
    public void FindContiguousMatch_FallsBackToBoundedPerWord_WhenDenseFails()
    {
        // Dense match fails (completely different character encoding) but per-word
        // fallback finds words and bounds them spatially to the right region.
        var words = new List<WordBoundingBox>
        {
            // Left column (line 1)
            new("belangrijk", 10, 200, 80, 212),
            new("document", 85, 200, 140, 212),
            // Right column (line 1) — the target passage
            new("Zeer", 300, 200, 330, 212),
            new("belangrijk", 335, 200, 405, 212),
            new("rapport", 410, 200, 460, 212),
        };

        // The snippet "Zeer belangrijk rapport" — the dense match should find it in
        // the spatially-ordered string: "belangrijkdocumentZeerbelangrijkrapport".
        // If it doesn't (e.g. char encoding diff), the bounded fallback should
        // concentrate on the right column, not scatter across both columns.
        var indices = CoordinateTransformer.FindContiguousMatch("Zeer belangrijk rapport", words);

        indices.Should().Contain(2, "should match 'Zeer'");
        indices.Should().Contain(3, "should match right-column 'belangrijk'");
        indices.Should().Contain(4, "should match 'rapport'");
    }

    // --- ToDense tests ---

    [Fact]
    public void ToDense_Strips_Whitespace_And_ControlChars()
    {
        CoordinateTransformer.ToDense("Hello World").Should().Be("helloworld");
        CoordinateTransformer.ToDense("Q&A\nSystem").Should().Be("q&asystem");
        CoordinateTransformer.ToDense("  spaces  ").Should().Be("spaces");
    }
}
