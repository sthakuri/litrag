using Docnet.Core;
using Docnet.Core.Models;

namespace LitRag.Web.Services.Pdf;

public record ExtractedPage(int PageNumber, string Text);

public record ExtractedPdf(int PageCount, IReadOnlyList<ExtractedPage> Pages)
{
    public string FullText => string.Join("\n\n", Pages.Select(p => p.Text));
    public int TotalCharacters => Pages.Sum(p => p.Text.Length);
}

public class PdfTextExtractor
{
    // Rendering dimensions are required by Docnet.Core's API but unused for text-only extraction.
    private static readonly PageDimensions DummyDimensions = new(1, 1);

    public ExtractedPdf Extract(byte[] pdfBytes)
    {
        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, DummyDimensions);
        var pageCount = docReader.GetPageCount();
        var pages = new List<ExtractedPage>(pageCount);

        for (var i = 0; i < pageCount; i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            var text = pageReader.GetText() ?? string.Empty;
            pages.Add(new ExtractedPage(i + 1, text));
        }

        return new ExtractedPdf(pageCount, pages);
    }

    public bool TryExtract(byte[] pdfBytes, out ExtractedPdf? extracted, out string? error)
    {
        try
        {
            extracted = Extract(pdfBytes);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            extracted = null;
            error = $"Could not read this file as a PDF ({ex.Message}).";
            return false;
        }
    }
}
