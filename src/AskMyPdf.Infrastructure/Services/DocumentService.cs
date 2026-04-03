namespace AskMyPdf.Infrastructure.Services;

using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;

public class DocumentService(BoundingBoxExtractor extractor, SqliteDb db)
{
    public async Task<Document> UploadAsync(Stream pdfStream, string fileName, long fileSize)
    {
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var pages = extractor.ExtractWordBounds(bytes);

        var doc = new Document(
            Id: Guid.NewGuid().ToString("N"),
            FileName: fileName,
            UploadedAt: DateTime.UtcNow,
            PageCount: pages.Count,
            FileSize: fileSize);

        await db.SaveDocumentAsync(doc, bytes, pages);

        return doc;
    }
}
