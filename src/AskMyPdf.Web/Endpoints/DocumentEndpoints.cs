namespace AskMyPdf.Web.Endpoints;

using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Web.Dtos;

public static class DocumentEndpoints
{
    // PDF magic bytes: "%PDF"
    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46];

    private static DocumentDto ToDto(Core.Models.Document d) =>
        new(d.Id, d.FileName, d.UploadedAt, d.PageCount, d.FileSize);

    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents");

        group.MapPost("/upload", async (IFormFile file, DocumentService svc, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AskMyPdf.DocumentEndpoints");
            if (file.Length == 0)
                return Results.BadRequest(new { error = "File is empty" });
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only PDF files are accepted" });
            if (file.Length > 32 * 1024 * 1024)
                return Results.BadRequest(new { error = "File exceeds 32MB limit" });

            // Read into memory for magic bytes check + processing
            using var ms = new MemoryStream();
            await using var fileStream = file.OpenReadStream();
            await fileStream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            // Validate PDF magic bytes
            if (bytes.Length < 4 || !bytes.AsSpan(0, 4).SequenceEqual(PdfMagicBytes))
                return Results.BadRequest(new { error = "File is not a valid PDF" });

            try
            {
                var doc = await svc.UploadAsync(bytes, file.FileName, file.Length);
                return Results.Ok(new UploadResponse(doc.Id, doc.FileName, doc.PageCount, doc.FileSize));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process PDF: {FileName}", file.FileName);
                return Results.BadRequest(new { error = "The uploaded file could not be processed. Please ensure it is a valid PDF." });
            }
        })
        .DisableAntiforgery();

        group.MapGet("/", async (SqliteDb db) =>
        {
            var docs = await db.GetAllDocumentsAsync();
            return Results.Ok(docs.Select(ToDto));
        });

        group.MapGet("/{id}", async (string id, SqliteDb db) =>
        {
            var doc = await db.GetDocumentAsync(id);
            return doc is null
                ? Results.NotFound()
                : Results.Ok(ToDto(doc));
        });

        group.MapGet("/{id}/file", async (string id, SqliteDb db) =>
        {
            var bytes = await db.GetFileAsync(id);
            return bytes is null
                ? Results.NotFound()
                : Results.File(bytes, "application/pdf");
        });

        group.MapDelete("/{id}", async (string id, SqliteDb db) =>
        {
            var deleted = await db.DeleteDocumentAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }
}
