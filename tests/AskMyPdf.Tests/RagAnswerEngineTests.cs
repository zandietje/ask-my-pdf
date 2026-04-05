namespace AskMyPdf.Tests;

using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

public class RagAnswerEngineTests
{
    // --- ExtractCitations ---

    [Fact]
    public void ExtractCitations_FindsChunkReferences()
    {
        var chunkMap = new Dictionary<int, DocumentChunk>
        {
            [0] = new("doc1", 1, 0, "First chunk text"),
            [3] = new("doc1", 2, 3, "Third chunk text"),
            [5] = new("doc1", 3, 5, "Fifth chunk text"),
        };

        var answer = "Revenue grew [C0] while costs decreased [C3]. See also [C5].";
        var citations = RagAnswerEngine.ExtractCitations(answer, chunkMap, "test.pdf", "doc1");

        citations.Should().HaveCount(3);
        citations[0].PageNumber.Should().Be(1);
        citations[0].CitedText.Should().Be("First chunk text");
        citations[0].ChunkIndex.Should().Be(0);
        citations[1].PageNumber.Should().Be(2);
        citations[2].PageNumber.Should().Be(3);
    }

    [Fact]
    public void ExtractCitations_DeduplicatesRepeatedReferences()
    {
        var chunkMap = new Dictionary<int, DocumentChunk>
        {
            [1] = new("doc1", 1, 1, "Chunk text"),
        };

        var answer = "Point A [C1] and Point B [C1] both supported.";
        var citations = RagAnswerEngine.ExtractCitations(answer, chunkMap, "test.pdf", "doc1");

        citations.Should().HaveCount(1);
    }

    [Fact]
    public void ExtractCitations_IgnoresUnknownChunkIndices()
    {
        var chunkMap = new Dictionary<int, DocumentChunk>
        {
            [0] = new("doc1", 1, 0, "Known chunk"),
        };

        var answer = "Text [C0] and [C99] reference.";
        var citations = RagAnswerEngine.ExtractCitations(answer, chunkMap, "test.pdf", "doc1");

        citations.Should().HaveCount(1);
        citations[0].ChunkIndex.Should().Be(0);
    }

    [Fact]
    public void ExtractCitations_NoCitations_ReturnsEmpty()
    {
        var chunkMap = new Dictionary<int, DocumentChunk>();
        var citations = RagAnswerEngine.ExtractCitations(
            "No citations here.", chunkMap, "test.pdf", "doc1");

        citations.Should().BeEmpty();
    }

    // --- ReciprocalRankFusion ---

    [Fact]
    public void ReciprocalRankFusion_MergesRankedLists()
    {
        var chunkMap = Enumerable.Range(0, 10)
            .ToDictionary(i => i, i => new DocumentChunk("doc", 1, i, $"Chunk {i}"));

        var ftsResults = new List<DocumentChunk> { chunkMap[1], chunkMap[3], chunkMap[5] };
        var vectorResults = new List<(int, double)> { (3, 0.9), (7, 0.8), (1, 0.7) };

        var merged = RagAnswerEngine.ReciprocalRankFusion(ftsResults, vectorResults, chunkMap);

        // Chunk 3 and 1 appear in both lists → should rank highest
        merged[0].ChunkIndex.Should().Be(3); // rank 2 in FTS + rank 1 in vector
        merged[1].ChunkIndex.Should().Be(1); // rank 1 in FTS + rank 3 in vector
        merged.Should().HaveCount(4); // 1, 3, 5 from FTS + 7 from vector
    }

    [Fact]
    public void ReciprocalRankFusion_FtsOnly_ReturnsInFtsOrder()
    {
        var chunkMap = new Dictionary<int, DocumentChunk>
        {
            [2] = new("doc", 1, 2, "Chunk 2"),
            [4] = new("doc", 1, 4, "Chunk 4"),
        };

        var ftsResults = new List<DocumentChunk> { chunkMap[2], chunkMap[4] };
        var vectorResults = new List<(int, double)>();

        var merged = RagAnswerEngine.ReciprocalRankFusion(ftsResults, vectorResults, chunkMap);

        merged.Should().HaveCount(2);
        merged[0].ChunkIndex.Should().Be(2);
        merged[1].ChunkIndex.Should().Be(4);
    }

    // --- ExpandWithContext ---

    [Fact]
    public void ExpandWithContext_IncludesAdjacentChunks()
    {
        var chunkMap = Enumerable.Range(0, 10)
            .ToDictionary(i => i, i => new DocumentChunk("doc", 1, i, $"Chunk {i}"));

        var retrieved = new List<DocumentChunk> { chunkMap[3], chunkMap[7] };
        var expanded = RagAnswerEngine.ExpandWithContext(retrieved, chunkMap);

        // Should include 2,3,4 (around 3) and 6,7,8 (around 7)
        expanded.Should().Contain(c => c.ChunkIndex == 2);
        expanded.Should().Contain(c => c.ChunkIndex == 3);
        expanded.Should().Contain(c => c.ChunkIndex == 4);
        expanded.Should().Contain(c => c.ChunkIndex == 6);
        expanded.Should().Contain(c => c.ChunkIndex == 7);
        expanded.Should().Contain(c => c.ChunkIndex == 8);
        expanded.Should().HaveCount(6);
    }

    [Fact]
    public void ExpandWithContext_HandlesEdgeChunks()
    {
        var chunkMap = new Dictionary<int, DocumentChunk>
        {
            [0] = new("doc", 1, 0, "First"),
            [1] = new("doc", 1, 1, "Second"),
        };

        var retrieved = new List<DocumentChunk> { chunkMap[0] };
        var expanded = RagAnswerEngine.ExpandWithContext(retrieved, chunkMap);

        expanded.Should().HaveCount(2); // 0 and 1 (no -1 exists)
        expanded[0].ChunkIndex.Should().Be(0);
        expanded[1].ChunkIndex.Should().Be(1);
    }

    [Fact]
    public void ExpandWithContext_ReturnsSortedByChunkIndex()
    {
        var chunkMap = Enumerable.Range(0, 5)
            .ToDictionary(i => i, i => new DocumentChunk("doc", 1, i, $"Chunk {i}"));

        var retrieved = new List<DocumentChunk> { chunkMap[4], chunkMap[1] };
        var expanded = RagAnswerEngine.ExpandWithContext(retrieved, chunkMap);

        expanded.Should().BeInAscendingOrder(c => c.ChunkIndex);
    }

    [Fact]
    public void ExpandWithContext_DeduplicatesOverlappingWindows()
    {
        var chunkMap = Enumerable.Range(0, 5)
            .ToDictionary(i => i, i => new DocumentChunk("doc", 1, i, $"Chunk {i}"));

        // Chunks 1 and 2 are adjacent — their context windows overlap at chunks 1,2
        var retrieved = new List<DocumentChunk> { chunkMap[1], chunkMap[2] };
        var expanded = RagAnswerEngine.ExpandWithContext(retrieved, chunkMap);

        // Should be 0,1,2,3 without duplicates
        expanded.Should().HaveCount(4);
        expanded.Select(c => c.ChunkIndex).Should().BeEquivalentTo([0, 1, 2, 3]);
    }
}
