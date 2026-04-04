using AskMyPdf.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class ClaudeCliRunnerTests
{
    [Fact]
    public void ParseCliOutput_ValidEnvelope_ExtractsResult()
    {
        var json = """{"type":"result","subtype":"success","is_error":false,"result":"hello world"}""";
        ClaudeCliRunner.ParseCliOutput(json).Should().Be("hello world");
    }

    [Fact]
    public void ParseCliOutput_ErrorEnvelope_ReturnsNull()
    {
        var json = """{"type":"result","subtype":"error","is_error":true,"result":"something"}""";
        ClaudeCliRunner.ParseCliOutput(json).Should().BeNull();
    }

    [Fact]
    public void ParseCliOutput_EmptyString_ReturnsNull()
    {
        ClaudeCliRunner.ParseCliOutput("").Should().BeNull();
    }

    [Fact]
    public void ParseCliOutput_NotJson_ReturnsRaw()
    {
        ClaudeCliRunner.ParseCliOutput("just plain text").Should().Be("just plain text");
    }

    [Fact]
    public void ParseCliOutput_MissingResultField_ReturnsNull()
    {
        var json = """{"type":"result","is_error":false}""";
        ClaudeCliRunner.ParseCliOutput(json).Should().BeNull();
    }

    [Fact]
    public void CollapseWhitespace_MultiLineIndented_BecomesSingleLine()
    {
        var input = """
            Read the PDF file at /tmp/test.pdf.

            Answer this question: What is the capital?

            Rules:
            1. Be concise
            2. Cite sources
            """;

        var result = ClaudeCliRunner.CollapseWhitespace(input);
        result.Should().NotContain("\n");
        result.Should().NotContain("\r");
        result.Should().StartWith("Read the PDF");
        result.Should().Contain("Answer this question: What is the capital?");
        result.Should().Contain("1. Be concise 2. Cite sources");
    }
}
