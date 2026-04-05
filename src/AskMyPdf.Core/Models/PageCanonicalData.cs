namespace AskMyPdf.Core.Models;

/// <summary>
/// Canonical per-page representation with correct reading order.
/// Built once at ingestion time using PdfPig's document layout analysis.
/// CanonicalText contains the full page text; Tokens map each word to its
/// character offset in that text and its bounding box in PDF coordinates.
/// </summary>
public record PageCanonicalData(
    int PageNumber,
    double PageWidth,
    double PageHeight,
    string CanonicalText,
    List<PageToken> Tokens);
