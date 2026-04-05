using AskMyPdf.Infrastructure.Pdf;
using AskMyPdf.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class BoundingBoxExtractorTests
{
    private readonly BoundingBoxExtractor _extractor = new();

    [Fact]
    public void ExtractPages_Returns_Correct_PageCount()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractPages(pdfBytes);

        pages.Should().HaveCount(2);
    }

    [Fact]
    public void ExtractPages_Page1_Has_Tokens()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractPages(pdfBytes);
        var page1 = pages[0];

        page1.PageNumber.Should().Be(1);
        page1.Tokens.Should().NotBeEmpty();
        page1.Tokens.Select(t => t.Text).Should().Contain("Hello");
        page1.Tokens.Select(t => t.Text).Should().Contain("World");
    }

    [Fact]
    public void ExtractPages_Page2_Has_Tokens()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractPages(pdfBytes);
        var page2 = pages[1];

        page2.PageNumber.Should().Be(2);
        page2.Tokens.Should().NotBeEmpty();
        page2.Tokens.Select(t => t.Text).Should().Contain("Page");
    }

    [Fact]
    public void ExtractPages_PageDimensions_Are_Positive()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractPages(pdfBytes);

        foreach (var page in pages)
        {
            page.PageWidth.Should().BeGreaterThan(0);
            page.PageHeight.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void ExtractPages_BoundingBoxes_Are_Valid()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractPages(pdfBytes);

        foreach (var token in pages.SelectMany(p => p.Tokens))
        {
            token.Text.Should().NotBeNullOrEmpty();
            token.Right.Should().BeGreaterThan(token.Left, "Right should be greater than Left");
            token.Top.Should().BeGreaterThan(token.Bottom, "Top should be greater than Bottom (PdfPig Y-up)");
        }
    }

    [Fact]
    public void ExtractPages_CanonicalText_Contains_AllTokens()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();

        var pages = _extractor.ExtractPages(pdfBytes);

        foreach (var page in pages)
        {
            page.CanonicalText.Should().NotBeNullOrEmpty();
            foreach (var token in page.Tokens)
            {
                page.CanonicalText.Substring(token.Offset, token.Text.Length)
                    .Should().Be(token.Text, "token offset should point to correct position in canonical text");
            }
        }
    }
}
