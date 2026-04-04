using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AskMyPdf.Tests;

public class SqliteDbTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"askmypdf_test_{Guid.NewGuid():N}.db");

    private SqliteDb _db = null!;

    public async Task InitializeAsync()
    {
        _db = new SqliteDb(_dbPath);
        await _db.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private static Document CreateDoc(string id = "test-id") => new(
        id, "test.pdf", new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc), 2, 1024);

    private static readonly byte[] TestFileBytes = [0x25, 0x50, 0x44, 0x46, 0x01, 0x02, 0x03];

    private static List<PageBoundingData> CreateBounds() =>
    [
        new(1, 612, 792, [new WordBoundingBox("Hello", 10, 100, 50, 112)]),
        new(2, 612, 792, [new WordBoundingBox("World", 10, 200, 50, 212)]),
    ];

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        // Second call should not throw
        await _db.InitializeAsync();
    }

    [Fact]
    public async Task SaveAndGetDocument_RoundTrips_AllFields()
    {
        var doc = CreateDoc();
        await _db.SaveDocumentAsync(doc, TestFileBytes, CreateBounds());

        var loaded = await _db.GetDocumentAsync("test-id");

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(doc.Id);
        loaded.FileName.Should().Be(doc.FileName);
        loaded.UploadedAt.Should().Be(doc.UploadedAt);
        loaded.PageCount.Should().Be(doc.PageCount);
        loaded.FileSize.Should().Be(doc.FileSize);
    }

    [Fact]
    public async Task GetFileAsync_Returns_ExactBytes()
    {
        await _db.SaveDocumentAsync(CreateDoc(), TestFileBytes, CreateBounds());

        var bytes = await _db.GetFileAsync("test-id");

        bytes.Should().BeEquivalentTo(TestFileBytes);
    }

    [Fact]
    public async Task GetPageBoundsAsync_Deserializes_WordBoundingBoxes()
    {
        await _db.SaveDocumentAsync(CreateDoc(), TestFileBytes, CreateBounds());

        var pages = await _db.GetPageBoundsAsync("test-id");

        pages.Should().HaveCount(2);
        pages[0].PageNumber.Should().Be(1);
        pages[0].Words.Should().HaveCount(1);
        pages[0].Words[0].Text.Should().Be("Hello");
        pages[0].Words[0].Left.Should().Be(10);
        pages[1].Words[0].Text.Should().Be("World");
    }

    [Fact]
    public async Task GetPageBoundsAsync_Filtered_Returns_OnlyRequestedPages()
    {
        await _db.SaveDocumentAsync(CreateDoc(), TestFileBytes, CreateBounds());

        var pages = await _db.GetPageBoundsAsync("test-id", [2]);

        pages.Should().HaveCount(1);
        pages[0].PageNumber.Should().Be(2);
        pages[0].Words[0].Text.Should().Be("World");
    }

    [Fact]
    public async Task GetDocumentAsync_NonExistent_ReturnsNull()
    {
        var result = await _db.GetDocumentAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteDocumentAsync_RemovesAllData()
    {
        await _db.SaveDocumentAsync(CreateDoc(), TestFileBytes, CreateBounds());

        var deleted = await _db.DeleteDocumentAsync("test-id");

        deleted.Should().BeTrue();
        (await _db.GetDocumentAsync("test-id")).Should().BeNull();
        (await _db.GetFileAsync("test-id")).Should().BeNull();
        (await _db.GetPageBoundsAsync("test-id")).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteDocumentAsync_NonExistent_ReturnsFalse()
    {
        var result = await _db.DeleteDocumentAsync("nonexistent");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllDocumentsAsync_ReturnsSortedByUploadDate()
    {
        var older = new Document("a", "old.pdf", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, 100);
        var newer = new Document("b", "new.pdf", new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), 1, 200);

        await _db.SaveDocumentAsync(older, TestFileBytes, [new(1, 612, 792, [])]);
        await _db.SaveDocumentAsync(newer, TestFileBytes, [new(1, 612, 792, [])]);

        var all = await _db.GetAllDocumentsAsync();

        all.Should().HaveCount(2);
        all[0].Id.Should().Be("b", "newer document should be first (DESC order)");
        all[1].Id.Should().Be("a");
    }
}
