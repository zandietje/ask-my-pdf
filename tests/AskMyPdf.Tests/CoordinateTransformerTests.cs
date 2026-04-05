using System.Text;
using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class CoordinateTransformerTests
{
    private readonly CoordinateTransformer _transformer = new();

    /// <summary>
    /// Builds a PageCanonicalData from word definitions, mirroring BoundingBoxExtractor.AppendWordsAsLines:
    /// sorts by Y desc then X asc, groups into lines by Y tolerance, builds canonical text with char offsets.
    /// </summary>
    private static PageCanonicalData CreatePage(int pageNumber, double width, double height,
        params (string Text, double Left, double Bottom, double Right, double Top)[] wordDefs)
    {
        if (wordDefs.Length == 0)
            return new PageCanonicalData(pageNumber, width, height, "", []);

        const double lineTolerance = 2.0;

        var sorted = wordDefs
            .OrderByDescending(w => w.Top)
            .ThenBy(w => w.Left)
            .ToArray();

        var lines = new List<List<(string Text, double Left, double Bottom, double Right, double Top)>>
        {
            new() { sorted[0] }
        };

        for (var i = 1; i < sorted.Length; i++)
        {
            if (Math.Abs(sorted[i].Top - lines[^1][^1].Top) <= lineTolerance)
                lines[^1].Add(sorted[i]);
            else
                lines.Add([sorted[i]]);
        }

        var sb = new StringBuilder();
        var tokens = new List<PageToken>();

        for (var lineIdx = 0; lineIdx < lines.Count; lineIdx++)
        {
            if (lineIdx > 0) sb.Append('\n');
            var line = lines[lineIdx].OrderBy(w => w.Left).ToList();
            for (var wordIdx = 0; wordIdx < line.Count; wordIdx++)
            {
                if (wordIdx > 0) sb.Append(' ');
                var w = line[wordIdx];
                tokens.Add(new PageToken(w.Text, sb.Length, w.Left, w.Bottom, w.Right, w.Top));
                sb.Append(w.Text);
            }
        }

        return new PageCanonicalData(pageNumber, width, height, sb.ToString(), tokens);
    }

    // --- Coordinate conversion tests ---

    [Fact]
    public void ToHighlightAreas_Known_Coordinates_Produce_Correct_Percentages()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 100, 200, ("Hello", 10, 150, 30, 170))
        };

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
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 100, 200, ("Top", 0, 190, 50, 200))
        };

        var areas = _transformer.ToHighlightAreas("Top", 1, pages);

        areas.Should().HaveCount(1);
        areas[0].Top.Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void ToHighlightAreas_PageIndex_Is_ZeroBased()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200),
            CreatePage(2, 200, 200, ("Hello", 10, 100, 50, 112))
        };

        var areas = _transformer.ToHighlightAreas("Hello", 2, pages);

        areas.Should().HaveCount(1);
        areas[0].PageIndex.Should().Be(1);
    }

    // --- Text matching tests ---

    [Fact]
    public void ToHighlightAreas_TextMatching_Finds_Correct_Words()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("The", 10, 100, 30, 112),
                ("quick", 35, 100, 60, 112),
                ("brown", 65, 100, 90, 112),
                ("fox", 95, 100, 110, 112))
        };

        var areas = _transformer.ToHighlightAreas("quick brown", 1, pages);

        areas.Should().HaveCount(1);
        areas[0].Left.Should().BeApproximately(17.5, 0.01);
        areas[0].Width.Should().BeApproximately(27.5, 0.01);
    }

    [Fact]
    public void ToHighlightAreas_CaseInsensitive()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("Hello", 10, 100, 50, 112),
                ("WORLD", 55, 100, 95, 112))
        };

        _transformer.ToHighlightAreas("hello world", 1, pages).Should().HaveCount(1);
    }

    [Fact]
    public void ToHighlightAreas_Whitespace_Normalized()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("Hello", 10, 100, 50, 112),
                ("World", 55, 100, 95, 112))
        };

        _transformer.ToHighlightAreas("  Hello   World  ", 1, pages).Should().HaveCount(1);
    }

    [Fact]
    public void ToHighlightAreas_Newlines_In_CitedText()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("Ltd.", 10, 100, 40, 112),
                ("65", 45, 100, 55, 112),
                ("Chulia", 60, 100, 90, 112))
        };

        _transformer.ToHighlightAreas("Ltd.\n65 Chulia", 1, pages).Should().NotBeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_Tokenization_Differences()
    {
        // PdfPig splits "Q&A" into separate tokens
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("Document", 10, 100, 60, 112),
                ("Q", 65, 100, 72, 112),
                ("&", 73, 100, 78, 112),
                ("A", 79, 100, 86, 112))
        };

        _transformer.ToHighlightAreas("Document Q&A", 1, pages).Should().NotBeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_MultiLine_Produces_Multiple_Areas()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("First", 10, 100, 50, 112),
                ("line", 55, 100, 80, 112),
                ("second", 10, 78, 60, 90),
                ("line", 65, 78, 85, 90))
        };

        _transformer.ToHighlightAreas("First line\nsecond line", 1, pages)
            .Should().HaveCount(2);
    }

    [Fact]
    public void ToHighlightAreas_NoMatch_Returns_Empty()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200, ("Hello", 10, 100, 50, 112))
        };

        _transformer.ToHighlightAreas("Nonexistent", 1, pages).Should().BeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_EmptyPage_Returns_Empty()
    {
        var pages = new List<PageCanonicalData> { CreatePage(1, 200, 200) };
        _transformer.ToHighlightAreas("anything", 1, pages).Should().BeEmpty();
    }

    [Fact]
    public void ToHighlightAreas_WrongPage_Returns_Empty()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200, ("Hello", 10, 100, 50, 112))
        };

        _transformer.ToHighlightAreas("Hello", 5, pages).Should().BeEmpty();
    }

    // --- Dense normalization tests ---

    [Fact]
    public void ToDenseNormalized_Strips_Whitespace_And_Lowercases()
    {
        CoordinateTransformer.ToDenseNormalized("  Hello   World  ")
            .Should().Be("helloworld");
    }

    [Fact]
    public void ToDenseNormalized_Strips_Diacritics()
    {
        // "première" with è (U+00E8) → "premiere"
        CoordinateTransformer.ToDenseNormalized("premi\u00e8re keer")
            .Should().Be("premierekeer");
    }

    [Fact]
    public void ToDenseNormalized_Decomposes_Ligatures()
    {
        // ﬁ (U+FB01) → "fi", ﬂ (U+FB02) → "fl" via NFKD
        CoordinateTransformer.ToDenseNormalized("\uFB01nd \uFB02ow")
            .Should().Be("findflow");
    }

    [Fact]
    public void ToHighlightAreas_Diacritics_Matched_Through_Pipeline()
    {
        // Page has accented text, citation has plain ASCII — should still match
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("premi\u00e8re", 10, 100, 60, 112),
                ("keer", 65, 100, 90, 112))
        };

        _transformer.ToHighlightAreas("premiere keer", 1, pages)
            .Should().HaveCount(1, "diacritics should not prevent matching");
    }

    [Fact]
    public void ToHighlightAreas_Ligatures_Matched_Through_Pipeline()
    {
        // Page has ligature ﬁ, citation has plain "fi" — should match via NFKD
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("\uFB01nd", 10, 100, 40, 112),
                ("the", 45, 100, 65, 112),
                ("\uFB02ow", 70, 100, 100, 112))
        };

        _transformer.ToHighlightAreas("find the flow", 1, pages)
            .Should().HaveCount(1, "ligatures should decompose and match via NFKD");
    }

    // --- Individual word matching (Strategy 3) tests ---

    [Fact]
    public void ToHighlightAreas_IndividualWords_Fallback_When_Substring_Fails()
    {
        // Words exist on page but not as a contiguous substring
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("The", 10, 100, 30, 112),
                ("revenue", 35, 100, 80, 112),
                ("grew", 85, 100, 110, 112),
                ("significantly", 10, 78, 80, 90),
                ("last", 85, 78, 105, 90),
                ("year", 110, 78, 135, 90))
        };

        // This exact phrase doesn't appear contiguously — words are reordered
        var areas = _transformer.ToHighlightAreas("year revenue significantly", 1, pages);

        areas.Should().NotBeEmpty("individual word matching should find each word separately");
    }

    [Fact]
    public void ToHighlightAreas_IndividualWords_Skips_Short_Words()
    {
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("A", 10, 100, 20, 112),
                ("revenue", 25, 100, 80, 112))
        };

        // "A" is too short (< 3 chars after normalization) — only "revenue" should match
        var areas = _transformer.ToHighlightAreas("is a of revenue", 1, pages);

        areas.Should().HaveCount(1);
    }

    [Fact]
    public void ToHighlightAreas_IndividualWords_Not_Used_When_Substring_Matches()
    {
        // Ensure Strategy 1 takes precedence — substring match returns contiguous highlight
        var pages = new List<PageCanonicalData>
        {
            CreatePage(1, 200, 200,
                ("quick", 10, 100, 40, 112),
                ("brown", 45, 100, 80, 112),
                ("fox", 85, 100, 105, 112))
        };

        var areas = _transformer.ToHighlightAreas("quick brown", 1, pages);

        areas.Should().HaveCount(1, "substring match should produce one contiguous area");
    }

    [Fact]
    public void FindIndividualWordMatches_Returns_FirstOccurrence_Only()
    {
        var page = CreatePage(1, 200, 200,
            ("revenue", 10, 100, 60, 112),
            ("grew", 65, 100, 90, 112),
            ("revenue", 10, 78, 60, 90));   // second occurrence of "revenue"

        var indices = CoordinateTransformer.FindIndividualWordMatches("revenue grew", page);

        // Should find "revenue" at index 0 (first occurrence) and "grew" at index 1
        indices.Should().BeEquivalentTo([0, 1]);
    }

    [Fact]
    public void FindIndividualWordMatches_Empty_When_No_Words_Long_Enough()
    {
        var page = CreatePage(1, 200, 200,
            ("Hello", 10, 100, 50, 112));

        var indices = CoordinateTransformer.FindIndividualWordMatches("a I to", page);

        indices.Should().BeEmpty("all words are too short after normalization");
    }

    // --- Line grouping tests ---

    [Fact]
    public void GroupTokensIntoLines_Groups_By_Y_Tolerance()
    {
        var tokens = new List<PageToken>
        {
            new("A", 0, 10, 100, 20, 112),
            new("B", 2, 25, 101, 35, 113),   // within tolerance (1 unit diff)
            new("C", 4, 10, 78, 20, 90),     // new line (22 units diff)
        };

        var lines = CoordinateTransformer.GroupTokensIntoLines(tokens);

        lines.Should().HaveCount(2);
        lines[0].Should().HaveCount(2);
        lines[1].Should().HaveCount(1);
        lines[0][0].Text.Should().Be("B", "B has higher Top (113) so comes first in desc sort");
        lines[0][1].Text.Should().Be("A");
        lines[1][0].Text.Should().Be("C");
    }
}
