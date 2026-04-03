using AskMyPdf.Infrastructure.Pdf;
using AskMyPdf.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class BoundingBoxExtractorTests
{
    private readonly BoundingBoxExtractor _extractor = new();

    [Fact]
    public void ExtractWordBounds_Returns_Correct_PageCount()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractWordBounds(pdfBytes);

        pages.Should().HaveCount(2);
    }

    [Fact]
    public void ExtractWordBounds_Page1_Has_Words()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractWordBounds(pdfBytes);
        var page1 = pages[0];

        page1.PageNumber.Should().Be(1);
        page1.Words.Should().NotBeEmpty();
        page1.Words.Select(w => w.Text).Should().Contain("Hello");
        page1.Words.Select(w => w.Text).Should().Contain("World");
    }

    [Fact]
    public void ExtractWordBounds_Page2_Has_Words()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractWordBounds(pdfBytes);
        var page2 = pages[1];

        page2.PageNumber.Should().Be(2);
        page2.Words.Should().NotBeEmpty();
        page2.Words.Select(w => w.Text).Should().Contain("Page");
    }

    [Fact]
    public void ExtractWordBounds_PageDimensions_Are_Positive()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractWordBounds(pdfBytes);

        foreach (var page in pages)
        {
            page.PageWidth.Should().BeGreaterThan(0);
            page.PageHeight.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void ExtractWordBounds_BoundingBoxes_Are_Valid()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractWordBounds(pdfBytes);

        foreach (var word in pages.SelectMany(p => p.Words))
        {
            word.Text.Should().NotBeNullOrEmpty();
            word.Right.Should().BeGreaterThan(word.Left, "Right should be greater than Left");
            word.Top.Should().BeGreaterThan(word.Bottom, "Top should be greater than Bottom (PdfPig Y-up)");
        }
    }
}
