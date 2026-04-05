using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class ReconstructPageTextTests
{
    [Fact]
    public void ReconstructPageText_SingleLine_Joins_With_Spaces()
    {
        var page = new PageBoundingData(1, 200, 200, [
            new WordBoundingBox("Hello", 10, 100, 50, 112),
            new WordBoundingBox("World", 55, 100, 95, 112),
        ]);

        var text = PageTextBuilder.ReconstructPageText(page);

        text.Should().Be("Hello World");
    }

    [Fact]
    public void ReconstructPageText_MultipleLines_Joins_With_Newlines()
    {
        var page = new PageBoundingData(1, 200, 200, [
            new WordBoundingBox("First", 10, 100, 50, 112),
            new WordBoundingBox("line", 55, 100, 80, 112),
            new WordBoundingBox("Second", 10, 78, 60, 90),
            new WordBoundingBox("line", 65, 78, 85, 90),
        ]);

        var text = PageTextBuilder.ReconstructPageText(page);

        text.Should().Be("First line\nSecond line");
    }

    [Fact]
    public void ReconstructPageText_EmptyPage_Returns_EmptyString()
    {
        var page = new PageBoundingData(1, 200, 200, []);

        PageTextBuilder.ReconstructPageText(page).Should().BeEmpty();
    }

    [Fact]
    public void GroupWordsIntoLines_Groups_By_Y_Tolerance()
    {
        var words = new List<WordBoundingBox>
        {
            new("A", 10, 100, 20, 112),
            new("B", 25, 101, 35, 113),   // within tolerance (1 unit diff)
            new("C", 10, 78, 20, 90),     // new line (22 units diff)
        };

        var lines = PageTextBuilder.GroupWordsIntoLines(words);

        lines.Should().HaveCount(2);
        lines[0].Should().HaveCount(2);
        lines[1].Should().HaveCount(1);
        lines[0][0].Text.Should().Be("A");
        lines[0][1].Text.Should().Be("B");
        lines[1][0].Text.Should().Be("C");
    }

    [Fact]
    public void CoordinateTransformer_Delegates_ReconstructPageText()
    {
        var page = new PageBoundingData(1, 200, 200, [
            new WordBoundingBox("Hello", 10, 100, 50, 112),
        ]);

        // Verify the delegating methods still work
        var fromTransformer = CoordinateTransformer.ReconstructPageText(page);
        var fromBuilder = PageTextBuilder.ReconstructPageText(page);

        fromTransformer.Should().Be(fromBuilder);
    }
}
