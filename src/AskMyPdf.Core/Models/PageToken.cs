namespace AskMyPdf.Core.Models;

/// <summary>
/// A single word extracted from a PDF page, positioned within the canonical page text.
/// Stored in reading order — the order a human would read the page.
/// </summary>
public record PageToken(
    string Text,            // Word text as extracted by PdfPig
    int Offset,             // Start char position in PageCanonicalData.CanonicalText
    double Left,            // Bounding box in PDF coordinates (bottom-left origin, Y up)
    double Bottom,
    double Right,
    double Top);
