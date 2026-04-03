namespace AskMyPdf.Core.Models;

public record PageBoundingData(
    int PageNumber,
    double PageWidth,
    double PageHeight,
    List<WordBoundingBox> Words);
