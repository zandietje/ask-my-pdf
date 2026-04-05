using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class ReconstructPageTextTests
{
    [Fact]
    public void ReconstructPageText_Returns_CanonicalText()
    {
        var page = new PageCanonicalData(1, 200, 200, "Hello World", [
            new PageToken("Hello", 0, 10, 100, 50, 112),
            new PageToken("World", 6, 55, 100, 95, 112),
        ]);

        PageTextBuilder.ReconstructPageText(page).Should().Be("Hello World");
    }

    [Fact]
    public void ReconstructPageText_MultipleLines_Preserves_Newlines()
    {
        var page = new PageCanonicalData(1, 200, 200, "First line\nSecond line", [
            new PageToken("First", 0, 10, 100, 50, 112),
            new PageToken("line", 6, 55, 100, 80, 112),
            new PageToken("Second", 11, 10, 78, 60, 90),
            new PageToken("line", 18, 65, 78, 85, 90),
        ]);

        PageTextBuilder.ReconstructPageText(page).Should().Be("First line\nSecond line");
    }

    [Fact]
    public void ReconstructPageText_EmptyPage_Returns_EmptyString()
    {
        var page = new PageCanonicalData(1, 200, 200, "", []);

        PageTextBuilder.ReconstructPageText(page).Should().BeEmpty();
    }
}
