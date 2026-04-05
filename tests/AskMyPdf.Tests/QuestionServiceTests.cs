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
    public async Task StreamAnswerAsync_WithNonFocusingEngine_YieldsTextAndResolvedCitations()
    {
        // Arrange: save a test document with known words
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();
        var extractor = new BoundingBoxExtractor();
        var pages = extractor.ExtractWordBounds(pdfBytes);
        var doc = new Document("test-id", "test.pdf", DateTime.UtcNow, pages.Count, pdfBytes.Length);
        await _documents.SaveDocumentAsync(doc, pdfBytes, pages);

        // Create a mock engine that returns known text + citation
        var mockEngine = new MockAnswerEngine(
            key: "mock",
            needsFocusing: false,
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
            [mockEngine], new MockCitationFocuser(), _documents, _transformer,
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
    public async Task StreamAnswerAsync_WithFocusingEngine_RunsFocusAndResolvesHighlights()
    {
        // Arrange
        var pdfBytes = TestPdfGenerator.CreateSimplePdf();
        var extractor = new BoundingBoxExtractor();
        var pages = extractor.ExtractWordBounds(pdfBytes);
        var doc = new Document("test-id", "test.pdf", DateTime.UtcNow, pages.Count, pdfBytes.Length);
        await _documents.SaveDocumentAsync(doc, pdfBytes, pages);

        var mockEngine = new MockAnswerEngine(
            key: "mock-focus",
            needsFocusing: true,
            events:
            [
                new AnswerStreamEvent.TextDelta("Focused answer."),
                new AnswerStreamEvent.CitationReceived(new Citation(
                    DocumentId: "", DocumentName: "test.pdf",
                    PageNumber: 1, CitedText: "Full page text here",
                    HighlightAreas: [])),
                new AnswerStreamEvent.Done(),
            ]);

        // Focuser returns a known snippet that exists in the test PDF
        var mockFocuser = new MockCitationFocuser { FocusResult = "Hello World" };

        var svc = new QuestionService(
            [mockEngine], mockFocuser, _documents, _transformer,
            NullLogger<QuestionService>.Instance);

        // Act
        var events = new List<AnswerStreamEvent>();
        await foreach (var evt in svc.StreamAnswerAsync("What?", "test-id", "mock-focus"))
            events.Add(evt);

        // Assert
        mockFocuser.CallCount.Should().BeGreaterThan(0, "focuser should be called for NeedsFocusing engines");

        var citation = events.OfType<AnswerStreamEvent.CitationReceived>().FirstOrDefault();
        citation.Should().NotBeNull();
        citation!.Citation.CitedText.Should().Be("Hello World");
        citation.Citation.HighlightAreas.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StreamAnswerAsync_DocumentNotFound_YieldsErrorText()
    {
        var mockEngine = new MockAnswerEngine("mock", false, []);
        var svc = new QuestionService(
            [mockEngine], new MockCitationFocuser(), _documents, _transformer,
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
        var pages = extractor.ExtractWordBounds(pdfBytes);
        var doc = new Document("test-id", "test.pdf", DateTime.UtcNow, pages.Count, pdfBytes.Length);
        await _documents.SaveDocumentAsync(doc, pdfBytes, pages);

        var mockEngine = new MockAnswerEngine("mock", false,
        [
            new AnswerStreamEvent.TextDelta("I could not find an answer."),
            new AnswerStreamEvent.Done(),
        ]);

        var svc = new QuestionService(
            [mockEngine], new MockCitationFocuser(), _documents, _transformer,
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
file class MockAnswerEngine(string key, bool needsFocusing, AnswerStreamEvent[] events) : IAnswerEngine
{
    public string DisplayName => key;
    public string Key => key;
    public bool NeedsFocusing => needsFocusing;

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

file class MockCitationFocuser : ICitationFocuser
{
    public string? FocusResult { get; set; }
    public int CallCount { get; private set; }

    public Task<string?> FocusCitationAsync(
        string pageText, string question, string fullAnswer, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(FocusResult);
    }
}
