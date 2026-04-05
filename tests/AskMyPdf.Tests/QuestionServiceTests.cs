namespace AskMyPdf.Tests;

using System.Runtime.CompilerServices;
using AskMyPdf.Core.Models;
using AskMyPdf.Core.Services;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class QuestionServiceTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"askmypdf_test_{Guid.NewGuid():N}.db");

    private DbConnectionFactory _dbFactory = null!;
    private DocumentRepository _documents = null!;
    private CoordinateTransformer _transformer = null!;

    public async Task InitializeAsync()
    {
        _dbFactory = new DbConnectionFactory(_dbPath);
        await _dbFactory.InitializeAsync();
        _documents = new DocumentRepository(_dbFactory);
        _transformer = new CoordinateTransformer();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task StreamAnswerAsync_YieldsTextAndResolvedCitations()
    {
        // Arrange: save a test document with known words
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();
        var extractor = new BoundingBoxExtractor();
        var pages = extractor.ExtractPages(pdfBytes);
        var doc = new Document("test-id", "test.pdf", DateTime.UtcNow, pages.Count, pdfBytes.Length);
        await _documents.SaveDocumentAsync(doc, pdfBytes, pages);

        // Create a mock engine that returns known text + citation
        var mockEngine = new MockAnswerEngine(
            key: "mock",
            events:
            [
                new AnswerStreamEvent.TextDelta("The answer is 42."),
                new AnswerStreamEvent.CitationReceived(new Citation(
                    DocumentId: "", DocumentName: "test.pdf",
                    PageNumber: 1, CitedText: "Hello World",
                    HighlightAreas: [])),
                new AnswerStreamEvent.Done(),
            ]);

        var svc = new QuestionService(
            [mockEngine], _documents, _transformer,
            NullLogger<QuestionService>.Instance);

        // Act
        var events = new List<AnswerStreamEvent>();
        await foreach (var evt in svc.StreamAnswerAsync("What?", "test-id", "mock"))
            events.Add(evt);

        // Assert
        events.Should().ContainSingle(e => e is AnswerStreamEvent.TextDelta);
        events.Should().ContainSingle(e => e is AnswerStreamEvent.CitationReceived);
        events.Should().ContainSingle(e => e is AnswerStreamEvent.Done);

        var citation = events.OfType<AnswerStreamEvent.CitationReceived>().First().Citation;
        citation.PageNumber.Should().Be(1);
        citation.DocumentId.Should().Be("test-id");
        citation.DocumentName.Should().Be("test.pdf");
        // Highlight areas should be resolved (non-empty if text was found on page)
        citation.HighlightAreas.Should().NotBeEmpty("'Hello World' exists on page 1 of the test PDF");
    }

    [Fact]
    public async Task StreamAnswerAsync_DocumentNotFound_YieldsErrorText()
    {
        var mockEngine = new MockAnswerEngine("mock", []);
        var svc = new QuestionService(
            [mockEngine], _documents, _transformer,
            NullLogger<QuestionService>.Instance);

        var events = new List<AnswerStreamEvent>();
        await foreach (var evt in svc.StreamAnswerAsync("What?", "nonexistent", "mock"))
            events.Add(evt);

        var text = events.OfType<AnswerStreamEvent.TextDelta>().First().Text;
        text.Should().Contain("not found", Exactly.Once(), "should indicate document was not found");
        events.Should().ContainSingle(e => e is AnswerStreamEvent.Done);
    }

    [Fact]
    public async Task StreamAnswerAsync_NoCitations_YieldsTextAndDone()
    {
        // Arrange: save a document
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();
        var extractor = new BoundingBoxExtractor();
        var pages = extractor.ExtractPages(pdfBytes);
        var doc = new Document("test-id", "test.pdf", DateTime.UtcNow, pages.Count, pdfBytes.Length);
        await _documents.SaveDocumentAsync(doc, pdfBytes, pages);

        var mockEngine = new MockAnswerEngine("mock",
        [
            new AnswerStreamEvent.TextDelta("I could not find an answer."),
            new AnswerStreamEvent.Done(),
        ]);

        var svc = new QuestionService(
            [mockEngine], _documents, _transformer,
            NullLogger<QuestionService>.Instance);

        // Act
        var events = new List<AnswerStreamEvent>();
        await foreach (var evt in svc.StreamAnswerAsync("What?", "test-id", "mock"))
            events.Add(evt);

        // Assert: text + done, no citations
        events.Should().ContainSingle(e => e is AnswerStreamEvent.TextDelta);
        events.Should().ContainSingle(e => e is AnswerStreamEvent.Done);
        events.Should().NotContain(e => e is AnswerStreamEvent.CitationReceived);
    }
}

// Test doubles — file-scoped so they're private to this file
file class MockAnswerEngine(string key, AnswerStreamEvent[] events) : IAnswerEngine
{
    public string DisplayName => key;
    public string Key => key;

    public async IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
        string question, byte[] pdfBytes, string fileName, string documentId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.Yield();
        }
    }
}
