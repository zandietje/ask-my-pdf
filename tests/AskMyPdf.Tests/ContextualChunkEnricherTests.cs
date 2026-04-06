namespace AskMyPdf.Tests;

using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

public class ContextualChunkEnricherTests
{
    [Fact]
    public void SearchableText_Returns_Enriched_When_Available()
    {
        var chunk = new DocumentChunk("doc1", 1, 0, "original", "context prefix.\n\noriginal");
        chunk.SearchableText.Should().Be("context prefix.\n\noriginal");
    }

    [Fact]
    public void SearchableText_Returns_ChunkText_When_No_Enrichment()
    {
        var chunk = new DocumentChunk("doc1", 1, 0, "original");
        chunk.SearchableText.Should().Be("original");
    }

    [Fact]
    public void SearchableText_Returns_ChunkText_When_EnrichedText_Is_Null()
    {
        var chunk = new DocumentChunk("doc1", 1, 0, "original", null);
        chunk.SearchableText.Should().Be("original");
    }

    [Fact]
    public void DocumentChunk_With_Expression_Preserves_Original_Text()
    {
        var original = new DocumentChunk("doc1", 1, 0, "original text");
        var enriched = original with { EnrichedText = "context.\n\noriginal text" };

        enriched.ChunkText.Should().Be("original text");
        enriched.EnrichedText.Should().Be("context.\n\noriginal text");
        enriched.SearchableText.Should().Be("context.\n\noriginal text");
    }

    [Fact]
    public void ParseBatchResponse_Parses_Numbered_Contexts()
    {
        var response = """
            [1] This chunk discusses the company's founding in 2015 by Jane Smith.
            [2] This section covers Q3 2024 revenue figures and growth metrics.
            [3] This describes the product roadmap for the AI division.
            """;

        var result = ContextualChunkEnricher.ParseBatchResponse(response, 3);

        result.Should().HaveCount(3);
        result[0].Should().Contain("founding in 2015");
        result[1].Should().Contain("Q3 2024 revenue");
        result[2].Should().Contain("product roadmap");
    }

    [Fact]
    public void ParseBatchResponse_Handles_Missing_Contexts()
    {
        var response = """
            [1] Context for first chunk.
            [3] Context for third chunk.
            """;

        var result = ContextualChunkEnricher.ParseBatchResponse(response, 3);

        result.Should().HaveCount(3);
        result[0].Should().Contain("first chunk");
        result[1].Should().BeEmpty();
        result[2].Should().Contain("third chunk");
    }

    [Fact]
    public void ParseBatchResponse_Falls_Back_To_Line_Splitting()
    {
        // No [N] prefixes — fallback to line-by-line
        var response = """
            Context about the company overview section.
            Context about financial performance in 2024.
            """;

        var result = ContextualChunkEnricher.ParseBatchResponse(response, 2);

        result.Should().HaveCount(2);
        result[0].Should().Contain("company overview");
        result[1].Should().Contain("financial performance");
    }

    [Fact]
    public void ParseBatchResponse_Handles_Multiline_Contexts()
    {
        var response = "[1] This discusses the merger between Company A and Company B in March 2024.\n[2] This covers the regulatory approval process for the new drug candidate.";

        var result = ContextualChunkEnricher.ParseBatchResponse(response, 2);

        result.Should().HaveCount(2);
        result[0].Should().Contain("merger");
        result[1].Should().Contain("regulatory approval");
    }

    [Fact]
    public void ParseBatchResponse_Returns_Empty_Strings_For_Empty_Response()
    {
        var result = ContextualChunkEnricher.ParseBatchResponse("", 3);

        result.Should().HaveCount(3);
        result.Should().AllBe("");
    }
}
