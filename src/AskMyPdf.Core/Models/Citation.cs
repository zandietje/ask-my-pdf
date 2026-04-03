namespace AskMyPdf.Core.Models;

public record Citation(
    string DocumentId,
    string DocumentName,
    int PageNumber,
    string CitedText,
    List<HighlightArea> HighlightAreas);
