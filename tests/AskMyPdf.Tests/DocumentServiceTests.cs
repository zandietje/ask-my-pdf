using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AskMyPdf.Tests;

public class DocumentServiceTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"askmypdf_test_{Guid.NewGuid():N}.db");

    private DocumentService _svc = null!;
    private DocumentRepository _docs = null!;

    public async Task InitializeAsync()
    {
        var dbFactory = new DbConnectionFactory(_dbPath);
        await dbFactory.InitializeAsync();
        _docs = new DocumentRepository(dbFactory);
        var chunks = new ChunkRepository(dbFactory);
        // EmbeddingService with no API key → embeddings disabled, FTS5-only
        var embeddingOptions = new EmbeddingOptions();
        var embeddingService = new EmbeddingService(new HttpClient(), embeddingOptions, NullLogger<EmbeddingService>.Instance);
        _svc = new DocumentService(
            new BoundingBoxExtractor(), new DocumentChunker(), embeddingService, _docs, chunks,
            NullLogger<DocumentService>.Instance);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UploadAsync_ValidPdf_ReturnsDocumentWithCorrectPageCount()
    {
        var pdfBytes = TestPdfGenerator.CreateSimplePdf(); // 2 pages

        var doc = await _svc.UploadAsync(pdfBytes, "test.pdf", pdfBytes.Length);

        doc.FileName.Should().Be("test.pdf");
        doc.PageCount.Should().Be(2);
        doc.Id.Should().NotBeNullOrEmpty();

        // Verify persisted to database
        var loaded = await _docs.GetDocumentAsync(doc.Id);
        loaded.Should().NotBeNull();
        loaded!.PageCount.Should().Be(2);
    }

    [Fact]
    public async Task UploadAsync_ExceedsPageLimit_ThrowsWithMessage()
    {
        var pdfBytes = TestPdfGenerator.CreatePdf(101);

        var act = () => _svc.UploadAsync(pdfBytes, "big.pdf", pdfBytes.Length);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*100-page limit*");
    }
}
