using LitRag.Web.Data.Entities;

namespace LitRag.Web.Services.Rag;

public static class VectorSearch
{
    public static IReadOnlyList<(PaperChunk Chunk, float Score)> TopK(
        IReadOnlyList<PaperChunk> chunks, float[] query, int k)
    {
        return chunks
            .Select(c => (Chunk: c, Score: CosineSimilarity(query, c.UnpackEmbedding())))
            .OrderByDescending(x => x.Score)
            .Take(k)
            .ToList();
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        float dot = 0, normA = 0, normB = 0;

        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
