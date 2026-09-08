using LitRag.Web.Data.Entities;
using LitRag.Web.Services.Ollama;

namespace LitRag.Web.Services.Rag;

public record EmbeddingProgress(int Done, int Total);

public class EmbeddingService(OllamaClient ollama, ILogger<EmbeddingService> logger)
{
    public async Task<List<PaperChunk>> EmbedChunksAsync(
        int paperId,
        IReadOnlyList<TextChunk> chunks,
        IProgress<EmbeddingProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new List<PaperChunk>(chunks.Count);
        var total = chunks.Count;
        progress?.Report(new EmbeddingProgress(0, total));

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            var vector = await ollama.EmbedAsync(chunk.Text, EmbeddingKind.Document, ct);

            result.Add(new PaperChunk
            {
                PaperId = paperId,
                ChunkIndex = chunk.ChunkIndex,
                PageNumber = chunk.PageNumber,
                Text = chunk.Text,
                Embedding = PaperChunk.PackEmbedding(vector)
            });

            progress?.Report(new EmbeddingProgress(result.Count, total));
        }

        logger.LogInformation("Embedded {Count} chunks for paper {PaperId}", result.Count, paperId);
        return result;
    }
}
