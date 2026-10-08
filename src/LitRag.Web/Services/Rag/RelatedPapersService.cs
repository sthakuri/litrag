using LitRag.Web.Data;
using LitRag.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LitRag.Web.Services.Rag;

public record RelatedPaper(Paper Paper, float Score);

public class RelatedPapersService(LitRagDbContext db)
{
    private const int DefaultTopK = 5;

    /// <summary>
    /// Finds other papers in the library whose content is most similar to the given paper, by
    /// averaging each paper's chunk embeddings into a single centroid vector and ranking the rest
    /// by cosine similarity to the target paper's centroid.
    /// </summary>
    public async Task<IReadOnlyList<RelatedPaper>> FindRelatedAsync(
        int paperId, int topK = DefaultTopK, CancellationToken ct = default)
    {
        var chunksByPaper = (await db.PaperChunks.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.PaperId)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (!chunksByPaper.TryGetValue(paperId, out var targetChunks) || targetChunks.Count == 0)
        {
            return [];
        }

        var targetVector = AverageEmbedding(targetChunks);

        var otherCentroids = chunksByPaper
            .Where(kv => kv.Key != paperId && kv.Value.Count > 0)
            .Select(kv => (PaperId: kv.Key, Vector: AverageEmbedding(kv.Value)))
            .ToList();

        var ranked = VectorSearch.TopK(otherCentroids, x => x.Vector, targetVector, topK);

        var rankedPaperIds = ranked.Select(r => r.Item.PaperId).ToList();
        var papers = await db.Papers
            .AsNoTracking()
            .Where(p => rankedPaperIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        return ranked
            .Where(r => papers.ContainsKey(r.Item.PaperId))
            .Select(r => new RelatedPaper(papers[r.Item.PaperId], r.Score))
            .ToList();
    }

    private static float[] AverageEmbedding(IReadOnlyList<PaperChunk> chunks)
    {
        var vectors = chunks.Select(c => c.UnpackEmbedding()).ToList();
        var dimensions = vectors[0].Length;
        var sum = new float[dimensions];

        foreach (var vector in vectors)
        {
            for (var i = 0; i < dimensions; i++)
            {
                sum[i] += vector[i];
            }
        }

        for (var i = 0; i < dimensions; i++)
        {
            sum[i] /= vectors.Count;
        }

        return sum;
    }
}
