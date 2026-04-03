namespace AskMyPdf.Infrastructure.Services;

using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;

public class DocumentService(BoundingBoxExtractor extractor, SqliteDb db)
{
    private const int MaxPageCount = 100;

    public async Task<Document> UploadAsync(byte[] pdfBytes, string fileName, long fileSize)
    {
        var pages = extractor.ExtractWordBounds(pdfBytes);

        if (pages.Count > MaxPageCount)
            throw new InvalidOperationException(
                $"PDF exceeds the {MaxPageCount}-page limit. Please upload a shorter document.");

        var doc = new Document(
            Id: Guid.NewGuid().ToString("N"),
            FileName: fileName,
            UploadedAt: DateTime.UtcNow,
            PageCount: pages.Count,
            FileSize: fileSize);

        await db.SaveDocumentAsync(doc, pdfBytes, pages);

        return doc;
    }
}
