namespace AskMyPdf.Web.Dtos;

public record UploadResponse(
    string Id,
    string FileName,
    int PageCount,
    long FileSize);
