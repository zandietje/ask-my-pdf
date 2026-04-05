namespace AskMyPdf.Infrastructure.Pdf;

using AskMyPdf.Core.Models;

/// <summary>
/// Provides page text from the canonical representation.
/// Reading order was solved at ingestion time — just return the stored text.
/// </summary>
public static class PageTextBuilder
{
    public static string ReconstructPageText(PageCanonicalData page) => page.CanonicalText;
}
