using AskMyPdf.Core.Models;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class SmokeTests
{
    [Fact]
    public void Document_Record_Creates_Successfully()
    {
        var doc = new Document("1", "test.pdf", DateTime.UtcNow, 5, 1024);

        doc.Id.Should().Be("1");
        doc.FileName.Should().Be("test.pdf");
        doc.PageCount.Should().Be(5);
        doc.FileSize.Should().Be(1024);
    }

    [Fact]
    public void AnswerStreamEvent_Discriminated_Union_Works()
    {
        AnswerStreamEvent evt = new AnswerStreamEvent.TextDelta("hello");

        evt.Should().BeOfType<AnswerStreamEvent.TextDelta>();
        ((AnswerStreamEvent.TextDelta)evt).Text.Should().Be("hello");
    }
}
