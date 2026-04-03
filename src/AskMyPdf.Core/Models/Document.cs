namespace AskMyPdf.Core.Models;

public record Document(
    string Id,
    string FileName,
    DateTime UploadedAt,
    int PageCount,
    long FileSize);
