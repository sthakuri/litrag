using LitRag.Web.Services.Pdf;

namespace LitRag.Web.Services.Rag;

public record TextChunk(int ChunkIndex, int PageNumber, string Text);

public class TextChunker
{
    private const int ChunkSize = 1000;
    private const int Overlap = 150;

    public List<TextChunk> Chunk(ExtractedPdf pdf)
    {
        var chunks = new List<TextChunk>();
        var index = 0;

        foreach (var page in pdf.Pages)
        {
            var text = page.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length <= ChunkSize)
            {
                chunks.Add(new TextChunk(index++, page.PageNumber, text));
                continue;
            }

            var start = 0;
            while (start < text.Length)
            {
                var length = Math.Min(ChunkSize, text.Length - start);
                chunks.Add(new TextChunk(index++, page.PageNumber, text.Substring(start, length)));

                if (start + length >= text.Length)
                {
                    break;
                }

                start += ChunkSize - Overlap;
            }
        }

        return chunks;
    }
}
