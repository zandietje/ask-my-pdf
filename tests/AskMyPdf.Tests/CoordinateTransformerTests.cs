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

    // --- ToDense tests ---

    [Fact]
    public void ToDense_Strips_Whitespace_And_ControlChars()
    {
        CoordinateTransformer.ToDense("Hello World").Should().Be("helloworld");
        CoordinateTransformer.ToDense("Q&A\nSystem").Should().Be("q&asystem");
        CoordinateTransformer.ToDense("  spaces  ").Should().Be("spaces");
    }
}
