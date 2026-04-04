using AskMyPdf.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class ClaudeCliEngineTests
{
    [Fact]
    public void ParseStructuredResult_ValidJson_Parses()
    {
        var json = """
        {
            "answer": "The company was founded in 2015.",
            "evidence": [
                {
                    "page": 5,
                    "snippets": ["Founded in 2015 by Jane Smith."]
                }
            ]
        }
        """;

        var result = ClaudeCliEngine.ParseStructuredResult(json);
        result.Should().NotBeNull();
        result!.Answer.Should().Be("The company was founded in 2015.");
        result.Evidence.Should().HaveCount(1);
        result.Evidence[0].Page.Should().Be(5);
        result.Evidence[0].Snippets.Should().ContainSingle("Founded in 2015 by Jane Smith.");
    }

    [Fact]
    public void ParseStructuredResult_WithMarkdownFences_Strips()
    {
        var text = """
        ```json
        {"answer":"test answer","evidence":[]}
        ```
        """;

        var result = ClaudeCliEngine.ParseStructuredResult(text);
        result.Should().NotBeNull();
        result!.Answer.Should().Be("test answer");
        result.Evidence.Should().BeEmpty();
    }

    [Fact]
    public void ParseStructuredResult_WithSurroundingText_ExtractsJson()
    {
        var text = """
        Here is the result:
        {"answer":"extracted","evidence":[{"page":1,"snippets":["text"]}]}
        That's the answer.
        """;

        var result = ClaudeCliEngine.ParseStructuredResult(text);
        result.Should().NotBeNull();
        result!.Answer.Should().Be("extracted");
    }

    [Fact]
    public void ParseStructuredResult_NoAnswer_ReturnsNull()
    {
        var json = """{"evidence":[]}""";
        ClaudeCliEngine.ParseStructuredResult(json).Should().BeNull();
    }

    [Fact]
    public void ParseStructuredResult_EmptyString_ReturnsNull()
    {
        ClaudeCliEngine.ParseStructuredResult("").Should().BeNull();
    }

    [Fact]
    public void ParseStructuredResult_GarbageText_ReturnsNull()
    {
        ClaudeCliEngine.ParseStructuredResult("this is not json at all").Should().BeNull();
    }

    [Fact]
    public void ParseStructuredResult_MultipleEvidenceEntries_ParsesAll()
    {
        var json = """
        {
            "answer": "Multiple sources confirm this.",
            "evidence": [
                {"page": 2, "snippets": ["First source."]},
                {"page": 7, "snippets": ["Second source.", "Also this."]}
            ]
        }
        """;

        var result = ClaudeCliEngine.ParseStructuredResult(json);
        result.Should().NotBeNull();
        result!.Evidence.Should().HaveCount(2);
        result.Evidence[0].Page.Should().Be(2);
        result.Evidence[1].Snippets.Should().HaveCount(2);
    }

    [Fact]
    public void ParseStructuredResult_EmptyEvidence_Parses()
    {
        var json = """{"answer":"No evidence found.","evidence":[]}""";

        var result = ClaudeCliEngine.ParseStructuredResult(json);
        result.Should().NotBeNull();
        result!.Answer.Should().Be("No evidence found.");
        result.Evidence.Should().BeEmpty();
    }
}
