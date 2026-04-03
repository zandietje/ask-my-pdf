namespace AskMyPdf.Core.Models;

public record HighlightArea(
    int PageIndex,
    double Left,
    double Top,
    double Width,
    double Height);
