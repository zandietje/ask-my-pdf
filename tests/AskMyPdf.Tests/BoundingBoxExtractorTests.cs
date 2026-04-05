using AskMyPdf.Core.Models;
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

    [Fact]
    public void ExtractPages_TwoColumnPdf_OffsetInvariantHolds()
    {
        var pdfBytes = TestPdfGenerator.CreateTwoColumnPdf();

        var pages = _extractor.ExtractPages(pdfBytes);

        pages.Should().HaveCount(1);
        var page = pages[0];
        page.Tokens.Should().HaveCountGreaterOrEqualTo(4, "should have tokens from both columns");

        // Verify offset invariant for every token
        foreach (var token in page.Tokens)
        {
            page.CanonicalText.Substring(token.Offset, token.Text.Length)
                .Should().Be(token.Text,
                    $"token '{token.Text}' at offset {token.Offset} must match canonical text");
        }

        // Verify tokens are sorted by offset (monotonically increasing)
        for (var i = 1; i < page.Tokens.Count; i++)
        {
            page.Tokens[i].Offset.Should().BeGreaterThan(page.Tokens[i - 1].Offset,
                $"token offsets must be strictly increasing (token {i - 1} → {i})");
        }
    }

    [Fact]
    public void ExtractPages_ChunkFindability_AllChunksInCanonicalText()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();
        var pages = _extractor.ExtractPages(pdfBytes);
        var chunker = new DocumentChunker();
        var chunks = chunker.ChunkDocument("test", pages);

        chunks.Should().NotBeEmpty("simple PDF should produce at least one chunk");

        var pageMap = pages.ToDictionary(p => p.PageNumber);
        foreach (var chunk in chunks)
        {
            var page = pageMap[chunk.PageNumber];
            var chunkDense = CoordinateTransformer.ToDenseNormalized(chunk.ChunkText);
            var pageDense = CoordinateTransformer.ToDenseNormalized(page.CanonicalText);

            pageDense.Should().Contain(chunkDense,
                $"chunk {chunk.ChunkId} (page {chunk.PageNumber}) must be findable in page canonical text");
        }
    }

    [Fact]
    public void ExtractPages_TwoColumnPdf_ChunkFindability()
    {
        var pdfBytes = TestPdfGenerator.CreateTwoColumnPdf();
        var pages = _extractor.ExtractPages(pdfBytes);
        var chunker = new DocumentChunker();
        var chunks = chunker.ChunkDocument("test", pages);

        chunks.Should().NotBeEmpty();

        var pageMap = pages.ToDictionary(p => p.PageNumber);
        foreach (var chunk in chunks)
        {
            var page = pageMap[chunk.PageNumber];
            var chunkDense = CoordinateTransformer.ToDenseNormalized(chunk.ChunkText);
            var pageDense = CoordinateTransformer.ToDenseNormalized(page.CanonicalText);

            pageDense.Should().Contain(chunkDense,
                $"chunk {chunk.ChunkId} (page {chunk.PageNumber}) must be findable in two-column page");
        }
    }
}
