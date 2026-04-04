namespace AskMyPdf.Tests.Fixtures;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

public static class TestPdfGenerator
{
    /// <summary>
    /// Creates a simple 2-page PDF with known text content for testing.
    /// Page 1: "Hello World" and "This is a test document"
    /// Page 2: "Page two content here"
    /// </summary>
    public static byte[] CreateSimplePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Page 1
        var page1 = builder.AddPage(PageSize.A4);
        page1.AddText("Hello World", 12, new PdfPoint(72, 700), font);
        page1.AddText("This is a test document", 12, new PdfPoint(72, 680), font);

        // Page 2
        var page2 = builder.AddPage(PageSize.A4);
        page2.AddText("Page two content here", 12, new PdfPoint(72, 700), font);

        return builder.Build();
    }

    /// <summary>
    /// Creates a PDF with the specified number of pages.
    /// Each page contains "Page N content" text.
    /// </summary>
    public static byte[] CreatePdf(int pageCount)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        for (var i = 1; i <= pageCount; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText($"Page {i} content", 12, new PdfPoint(72, 700), font);
        }

        return builder.Build();
    }
}
