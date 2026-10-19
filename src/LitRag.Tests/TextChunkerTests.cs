using LitRag.Web.Services.Pdf;
using LitRag.Web.Services.Rag;

namespace LitRag.Tests;

// Tests for TextChunker.Chunk.
// Chunker settings: ChunkSize = 1000 chars, Overlap = 150 chars, so each new chunk starts 850 chars after the previous one.
public class TextChunkerTests
{
    private readonly TextChunker _chunker = new();

    private static ExtractedPdf Pdf(params string[] pageTexts) =>
        new(pageTexts.Length, pageTexts.Select((t, i) => new ExtractedPage(i + 1, t)).ToList());

    // Non-repeating-ish text so substring checks are meaningful.
    private static string Text(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(i => (char)('a' + (i * 7 + i / 26) % 26)));

    [Fact]
    public void Chunk_NoPages_ReturnsEmptyList()
    {
        var result = _chunker.Chunk(Pdf());

        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_BlankOrWhitespacePages_AreSkipped()
    {
        var result = _chunker.Chunk(Pdf("", "   \n\t  "));

        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_ShortPage_ReturnsSingleTrimmedChunk()
    {
        var result = _chunker.Chunk(Pdf("   Abstract: this paper studies Rust.  \n"));

        var chunk = Assert.Single(result);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal(1, chunk.PageNumber);
        Assert.Equal("Abstract: this paper studies Rust.", chunk.Text);
    }

    [Fact]
    public void Chunk_PageExactlyChunkSize_ReturnsSingleChunk()
    {
        var text = Text(1000);

        var result = _chunker.Chunk(Pdf(text));

        var chunk = Assert.Single(result);
        Assert.Equal(text, chunk.Text);
    }

    [Fact]
    public void Chunk_PageOneCharOverChunkSize_SplitsIntoTwoOverlappingChunks()
    {
        var text = Text(1001);

        var result = _chunker.Chunk(Pdf(text));

        Assert.Equal(2, result.Count);
        Assert.Equal(text.Substring(0, 1000), result[0].Text);
        Assert.Equal(text.Substring(850), result[1].Text);   // starts 850 in, 151 chars long
        Assert.Equal(151, result[1].Text.Length);
    }

    [Fact]
    public void Chunk_LongPage_ProducesExpectedChunksWithOverlap()
    {
        var text = Text(2500);

        var result = _chunker.Chunk(Pdf(text));

        // Starts at 0, 850, 1700 -> lengths 1000, 1000, 800
        Assert.Equal(3, result.Count);
        Assert.Equal(text.Substring(0, 1000), result[0].Text);
        Assert.Equal(text.Substring(850, 1000), result[1].Text);
        Assert.Equal(text.Substring(1700), result[2].Text);

        // Last 150 chars of each chunk equal first 150 chars of the next
        for (var i = 0; i < result.Count - 1; i++)
        {
            Assert.Equal(result[i].Text[^150..], result[i + 1].Text[..150]);
        }
    }

    [Fact]
    public void Chunk_LongPage_NoChunkExceedsChunkSize_AndAllTextIsCovered()
    {
        var text = Text(5000);

        var result = _chunker.Chunk(Pdf(text));

        Assert.All(result, c => Assert.InRange(c.Text.Length, 1, 1000));
        Assert.EndsWith(result[^1].Text, text);
        Assert.StartsWith(result[0].Text, text);
    }

    [Fact]
    public void Chunk_MultiplePages_KeepsPageNumbersAndContinuousIndexes()
    {
        var longPage = Text(1200);   // -> 2 chunks

        var result = _chunker.Chunk(Pdf("Page one text", "", longPage, "Page four text"));

        Assert.Equal(4, result.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Select(c => c.ChunkIndex));
        Assert.Equal(new[] { 1, 3, 3, 4 }, result.Select(c => c.PageNumber)); // page 2 is empty and skipped
        Assert.Equal("Page one text", result[0].Text);
        Assert.Equal("Page four text", result[3].Text);
    }
}
