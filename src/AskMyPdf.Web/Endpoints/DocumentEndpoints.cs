namespace AskMyPdf.Web.Endpoints;

using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Web.Dtos;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents");

        group.MapPost("/upload", async (IFormFile file, DocumentService svc) =>
        {
            if (file.Length == 0)
                return Results.BadRequest(new { error = "File is empty" });
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only PDF files are accepted" });
            if (file.Length > 32 * 1024 * 1024)
                return Results.BadRequest(new { error = "File exceeds 32MB limit" });

            await using var stream = file.OpenReadStream();
            var doc = await svc.UploadAsync(stream, file.FileName, file.Length);

            return Results.Ok(new UploadResponse(doc.Id, doc.FileName, doc.PageCount, doc.FileSize));
        })
        .DisableAntiforgery();

        group.MapGet("/", async (SqliteDb db) =>
        {
            var docs = await db.GetAllDocumentsAsync();
            return Results.Ok(docs.Select(d =>
                new DocumentDto(d.Id, d.FileName, d.UploadedAt, d.PageCount, d.FileSize)));
        });

        group.MapGet("/{id}", async (string id, SqliteDb db) =>
        {
            var doc = await db.GetDocumentAsync(id);
            return doc is null
                ? Results.NotFound()
                : Results.Ok(new DocumentDto(doc.Id, doc.FileName, doc.UploadedAt, doc.PageCount, doc.FileSize));
        });

        group.MapGet("/{id}/file", async (string id, SqliteDb db) =>
        {
            var bytes = await db.GetFileAsync(id);
            return bytes is null
                ? Results.NotFound()
                : Results.File(bytes, "application/pdf");
        });
    }
}
