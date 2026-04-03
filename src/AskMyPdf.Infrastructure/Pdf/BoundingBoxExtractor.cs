namespace AskMyPdf.Infrastructure.Pdf;

using AskMyPdf.Core.Models;
using UglyToad.PdfPig;

public class BoundingBoxExtractor
{
    public List<PageBoundingData> ExtractWordBounds(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var pages = new List<PageBoundingData>(document.NumberOfPages);

        for (var i = 1; i <= document.NumberOfPages; i++)
        {
            var page = document.GetPage(i);
            var words = page.GetWords()
                .Select(w => new WordBoundingBox(
                    w.Text,
                    w.BoundingBox.Left,
                    w.BoundingBox.Bottom,
                    w.BoundingBox.Right,
                    w.BoundingBox.Top))
                .ToList();

            pages.Add(new PageBoundingData(i, page.Width, page.Height, words));
        }

        return pages;
    }
}
