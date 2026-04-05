namespace AskMyPdf.Tests;

using AskMyPdf.Infrastructure.Data;
using FluentAssertions;
using Xunit;

public class ChunkRepositoryTests
{
    [Theory]
    [InlineData("C++ programming", "\"programming\"")]  // ++ stripped, C too short
    [InlineData("(nested) query", "\"nested\" OR \"query\"")]
    [InlineData("test*", "\"test\"")]
    [InlineData("\"quoted\"", "\"quoted\"")]
    [InlineData("hello world", "\"hello\" OR \"world\"")]
    [InlineData("search-term", "\"searchterm\"")]
    [InlineData("a^b+c", "\"abc\"")]
    public void SanitizeFtsQuery_StripsSpecialCharacters(string input, string expected)
    {
        ChunkRepository.SanitizeFtsQuery(input).Should().Be(expected);
    }

    [Fact]
    public void SanitizeFtsQuery_AllStopWords_FallsBackToQuotedQuery()
    {
        // "is the" — both are stop words, falls back to full query
        var result = ChunkRepository.SanitizeFtsQuery("is the");
        result.Should().StartWith("\"").And.EndWith("\"");
    }

    [Fact]
    public void SanitizeFtsQuery_EmptyTokensAfterStripping_FallsBackToQuotedQuery()
    {
        // Single short token after stripping → falls back
        var result = ChunkRepository.SanitizeFtsQuery("C++");
        result.Should().StartWith("\"").And.EndWith("\"");
    }

    [Fact]
    public void SanitizeFtsQuery_MixedSpecialChars_ProducesValidQuery()
    {
        var result = ChunkRepository.SanitizeFtsQuery("C++ (test) *wildcard* search");
        // "test", "wildcard", "search" survive (C too short after stripping ++)
        result.Should().Be("\"test\" OR \"wildcard\" OR \"search\"");
    }
}
