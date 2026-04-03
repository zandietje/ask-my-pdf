namespace AskMyPdf.Web.Dtos;

public record DocumentDto(
    string Id,
    string FileName,
    DateTime UploadedAt,
    int PageCount,
    long FileSize);
