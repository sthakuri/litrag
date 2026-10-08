using LitRag.Web.Data;
using LitRag.Web.Services.Ollama;
using Microsoft.EntityFrameworkCore;

namespace LitRag.Web.Services.Summary;

public class SummaryService(OllamaClient ollama, LitRagDbContext db, ILogger<SummaryService> logger)
{
    // The paper's opening chunks (title, abstract, introduction) carry most of what's needed for a
    // decent summary without feeding a local model's whole context window.
    private const int ChunksToSummarize = 8;

    private const string SystemPrompt = """
        You are a research assistant. Write a concise summary (3-5 sentences, plain prose, no bullet
        points or headings) of the academic paper excerpted below, covering its main problem, approach,
        and key findings. Do not include citations, page references, or any preamble like "This paper" —
        respond with the summary text only.
        """;

    /// <summary>Generates a summary from the paper's first indexed chunks and saves it to Paper.Summary.</summary>
    public async Task<string> GenerateSummaryAsync(int paperId, CancellationToken ct = default)
    {
        var paper = await db.Papers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        var chunks = await db.PaperChunks
            .AsNoTracking()
            .Where(c => c.PaperId == paperId)
            .OrderBy(c => c.ChunkIndex)
            .Take(ChunksToSummarize)
            .ToListAsync(ct);

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException(
                "This paper hasn't been indexed yet, so there's no text available to summarize.");
        }

        var excerpt = string.Join("\n\n", chunks.Select(c => c.Text));
        var summary = (await ollama.GenerateTextAsync(SystemPrompt, excerpt, ct)).Trim();

        paper.Summary = summary;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Generated summary for paper {PaperId} from {Count} chunks", paperId, chunks.Count);
        return summary;
    }
}
