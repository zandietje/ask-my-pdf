using AskMyPdf.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

namespace AskMyPdf.Tests;

public class FocusCitationParsingTests
{
    [Fact]
    public void ParseFocusResponse_NormalText_ReturnsTrimmed()
    {
        ClaudeService.ParseFocusResponse("  The company was founded in 2015.  ")
            .Should().Be("The company was founded in 2015.");
    }

    [Fact]
    public void ParseFocusResponse_Null_ReturnsNull()
    {
        ClaudeService.ParseFocusResponse(null).Should().BeNull();
    }

    [Fact]
    public void ParseFocusResponse_Empty_ReturnsNull()
    {
        ClaudeService.ParseFocusResponse("").Should().BeNull();
    }

    [Fact]
    public void ParseFocusResponse_WhitespaceOnly_ReturnsNull()
    {
        ClaudeService.ParseFocusResponse("   \n\t  ").Should().BeNull();
    }

    [Fact]
    public void ParseFocusResponse_NoMatch_ReturnsNull()
    {
        ClaudeService.ParseFocusResponse("NO_MATCH").Should().BeNull();
    }

    [Fact]
    public void ParseFocusResponse_NoMatch_CaseInsensitive()
    {
        ClaudeService.ParseFocusResponse("no_match").Should().BeNull();
        ClaudeService.ParseFocusResponse("No_Match").Should().BeNull();
    }

    [Fact]
    public void ParseFocusResponse_AsciiQuotes_Stripped()
    {
        ClaudeService.ParseFocusResponse("\"The revenue grew by 15%.\"")
            .Should().Be("The revenue grew by 15%.");
    }

    [Fact]
    public void ParseFocusResponse_SmartQuotes_Stripped()
    {
        ClaudeService.ParseFocusResponse("\u201CThe revenue grew by 15%.\u201D")
            .Should().Be("The revenue grew by 15%.");
    }

    [Fact]
    public void ParseFocusResponse_MismatchedQuotes_NotStripped()
    {
        // Opening quote without matching closing quote should not be stripped
        ClaudeService.ParseFocusResponse("\"The revenue grew by 15%.")
            .Should().Be("\"The revenue grew by 15%.");
    }

    [Fact]
    public void ParseFocusResponse_MultiLine_PreservedIntact()
    {
        var input = "First relevant sentence.\nSecond relevant sentence.";
        ClaudeService.ParseFocusResponse(input).Should().Be(input);
    }
}
