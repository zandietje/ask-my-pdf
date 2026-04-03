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

    [Fact]
    public void ToHighlightAreas_Known_Coordinates_Produce_Correct_Percentages()
    {
        // Page: 100x200 units. Word at (10, 150, 30, 170) in PdfPig coords (left, bottom, right, top)
        var words = new List<WordBoundingBox>
        {
            new("Hello", 10, 150, 30, 170)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 100, 200, words) };

        var areas = _transformer.ToHighlightAreas("Hello", 1, pages);

        areas.Should().HaveCount(1);
        var area = areas[0];
        area.Left.Should().BeApproximately(10.0, 0.01);      // (10/100)*100
        area.Top.Should().BeApproximately(15.0, 0.01);       // ((200-170)/200)*100
        area.Width.Should().BeApproximately(20.0, 0.01);     // ((30-10)/100)*100
        area.Height.Should().BeApproximately(10.0, 0.01);    // ((170-150)/200)*100
    }

    [Fact]
    public void ToHighlightAreas_YAxis_Is_Flipped()
    {
        // Word near top of page in PdfPig (high Y) → low top_pct in viewer
        var words = new List<WordBoundingBox>
        {
            new("Top", 0, 190, 50, 200)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 100, 200, words) };

        var areas = _transformer.ToHighlightAreas("Top", 1, pages);

        areas.Should().HaveCount(1);
        areas[0].Top.Should().BeApproximately(0.0, 0.01); // ((200-200)/200)*100 = 0%
    }

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
        // Should span from "quick" (left=35) to "brown" (right=90)
        var area = areas[0];
        area.Left.Should().BeApproximately(17.5, 0.01);   // (35/200)*100
        area.Width.Should().BeApproximately(27.5, 0.01);   // ((90-35)/200)*100
    }

    [Fact]
    public void ToHighlightAreas_Whitespace_Normalization()
    {
        var words = new List<WordBoundingBox>
        {
            new("Hello", 10, 100, 50, 112),
            new("World", 55, 100, 95, 112)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        // Extra whitespace + control characters in cited text
        var areas = _transformer.ToHighlightAreas("  Hello   World  ", 1, pages);

        areas.Should().HaveCount(1);
    }

    [Fact]
    public void ToHighlightAreas_NoMatch_Returns_Empty()
    {
        var words = new List<WordBoundingBox>
        {
            new("Hello", 10, 100, 50, 112)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        var areas = _transformer.ToHighlightAreas("Nonexistent text", 1, pages);

        areas.Should().BeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_EmptyPage_Returns_Empty()
    {
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, []) };

        var areas = _transformer.ToHighlightAreas("anything", 1, pages);

        areas.Should().BeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_WrongPageNumber_Returns_Empty()
    {
        var words = new List<WordBoundingBox> { new("Hello", 10, 100, 50, 112) };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        var areas = _transformer.ToHighlightAreas("Hello", 5, pages);

        areas.Should().BeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_MultiLine_Produces_Multiple_Areas()
    {
        var words = new List<WordBoundingBox>
        {
            // Line 1 (top=112)
            new("First", 10, 100, 50, 112),
            new("line", 55, 100, 80, 112),
            // Line 2 (top=90, more than LineTolerance=2 apart from line 1)
            new("second", 10, 78, 60, 90),
            new("line", 65, 78, 85, 90)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        var areas = _transformer.ToHighlightAreas("First line second line", 1, pages);

        areas.Should().HaveCount(2, "words on two different lines should produce two highlight areas");
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
        areas[0].PageIndex.Should().Be(1, "pageNumber 2 should map to pageIndex 1 (0-based)");
    }

    [Fact]
    public void ToHighlightAreas_CaseInsensitive_Matching()
    {
        var words = new List<WordBoundingBox>
        {
            new("Hello", 10, 100, 50, 112),
            new("WORLD", 55, 100, 95, 112)
        };
        var pages = new List<PageBoundingData> { CreatePage(1, 200, 200, words) };

        var areas = _transformer.ToHighlightAreas("hello world", 1, pages);

        areas.Should().HaveCount(1);
    }

    [Fact]
    public void Normalize_Strips_ControlChars_And_Collapses_Whitespace()
    {
        var result = CoordinateTransformer.Normalize("Hello\u0002  \t World\n!");

        result.Should().Be("hello world!");
    }
}
