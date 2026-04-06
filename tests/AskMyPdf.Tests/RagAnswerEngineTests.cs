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
    public void ExtractCitations_HandlesCommaSeparatedRefs()
    {
        var chunkMap = new Dictionary<int, DocumentChunk>
        {
            [3] = new("doc1", 2, 3, "Third chunk"),
            [7] = new("doc1", 4, 7, "Seventh chunk"),
            [12] = new("doc1", 5, 12, "Twelfth chunk"),
        };

        var answer = "Multiple sources [C3, C7] and another group [C7, C12].";
        var citations = RagAnswerEngine.ExtractCitations(answer, chunkMap, "test.pdf", "doc1");

        citations.Should().HaveCount(3); // C3, C7, C12 (C7 deduplicated)
        citations.Select(c => c.ChunkIndex).Should().BeEquivalentTo([3, 7, 12]);
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

    // --- Citation narrowing ---

    [Fact]
    public void ExtractCitingSentence_Finds_Sentence_Before_Citation()
    {
        var answer = "The company grew significantly. Revenue increased by 15% [C3] while costs decreased.";
        var pos = answer.IndexOf("[C3]");
        var sentence = RagAnswerEngine.ExtractCitingSentence(answer, pos);
        sentence.Should().Be("Revenue increased by 15%");
    }

    [Fact]
    public void ExtractCitingSentence_Returns_Null_For_Short_Text()
    {
        var answer = "Yes [C1] confirmed.";
        var pos = answer.IndexOf("[C1]");
        var sentence = RagAnswerEngine.ExtractCitingSentence(answer, pos);
        // "Yes" is less than 15 chars
        sentence.Should().BeNull();
    }

    [Fact]
    public void ExtractCitingSentence_Returns_Null_At_Start()
    {
        var answer = "[C1] some text follows.";
        var sentence = RagAnswerEngine.ExtractCitingSentence(answer, 0);
        sentence.Should().BeNull();
    }

    [Fact]
    public void NarrowCitedText_Finds_Best_Matching_Sentence_In_Chunk()
    {
        var citingSentence = "Revenue increased by 15% year-over-year";
        var chunkText = "ACME Corp reported strong results. Revenue increased by 15% compared to the prior year. Operating margins also improved.";
        var narrowed = RagAnswerEngine.NarrowCitedText(citingSentence, chunkText);
        narrowed.Should().Contain("Revenue increased by 15%");
        narrowed.Length.Should().BeLessThan(chunkText.Length);
    }

    [Fact]
    public void NarrowCitedText_Falls_Back_To_Full_Chunk_When_No_Match()
    {
        var citingSentence = "Something completely unrelated to the chunk content here";
        var chunkText = "ACME Corp was founded in 2015. It operates in the fintech sector.";
        var narrowed = RagAnswerEngine.NarrowCitedText(citingSentence, chunkText);
        narrowed.Should().Be(chunkText);
    }

    [Fact]
    public void NarrowCitedText_Falls_Back_When_Sentence_Is_Null()
    {
        var chunkText = "Some chunk text here that is long enough.";
        RagAnswerEngine.NarrowCitedText(null, chunkText).Should().Be(chunkText);
    }

    [Fact]
    public void NarrowCitedText_Falls_Back_When_Sentence_Is_Short()
    {
        var chunkText = "Some chunk text here that is long enough.";
        RagAnswerEngine.NarrowCitedText("short", chunkText).Should().Be(chunkText);
    }

    [Fact]
    public void ExtractCitations_Uses_Narrowed_Text()
    {
        var chunkMap = new Dictionary<int, DocumentChunk>
        {
            [3] = new("doc1", 2, 3,
                "ACME Corp reported strong results. Revenue increased by 15% compared to the prior year. Operating margins also improved significantly."),
        };

        var answer = "The company reported strong financial performance. Revenue increased by 15% year-over-year [C3].";
        var citations = RagAnswerEngine.ExtractCitations(answer, chunkMap, "test.pdf", "doc1");

        citations.Should().HaveCount(1);
        // Should be narrowed to the matching sentence, not the full chunk
        citations[0].CitedText.Should().Contain("Revenue increased by 15%");
        citations[0].CitedText.Length.Should().BeLessThan(chunkMap[3].ChunkText.Length);
    }
}
