using LitRag.Web.Data;
using LitRag.Web.Data.Entities;
using LitRag.Web.Services.Ollama;
using Microsoft.EntityFrameworkCore;

namespace LitRag.Web.Services.Rag;

public record ChatAnswer(IAsyncEnumerable<string> TokenStream, IReadOnlyList<int> SourcePages);

public class RagChatService(OllamaClient ollama, LitRagDbContext db)
{
    private const int TopK = 8;
    private const int HistoryTurns = 6;

    public async Task<ChatAnswer> AskStreamingAsync(int paperId, string question, CancellationToken ct = default)
    {
        var chunks = await db.PaperChunks.AsNoTracking().Where(c => c.PaperId == paperId).ToListAsync(ct);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("This paper hasn't finished indexing yet — try again in a moment.");
        }

        var queryVector = await ollama.EmbedAsync(question, EmbeddingKind.Query, ct);
        var top = VectorSearch.TopK(chunks, queryVector, TopK);

        var history = await db.ChatMessages
            .AsNoTracking()
            .Where(m => m.PaperId == paperId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(HistoryTurns)
            .ToListAsync(ct);
        history.Reverse();

        var messages = new List<OllamaChatMessage> { new("system", BuildSystemPrompt(top)) };
        messages.AddRange(history.Select(m =>
            new OllamaChatMessage(m.Role == ChatRole.User ? "user" : "assistant", m.Content)));
        messages.Add(new OllamaChatMessage("user", question));

        var sourcePages = top.Select(t => t.Chunk.PageNumber).Distinct().OrderBy(p => p).ToList();
        return new ChatAnswer(ollama.ChatStreamAsync(messages, ct), sourcePages);
    }

    private static string BuildSystemPrompt(IReadOnlyList<(PaperChunk Chunk, float Score)> top)
    {
        var excerpts = string.Join("\n\n", top.Select(t => $"[p.{t.Chunk.PageNumber}] {t.Chunk.Text}"));
        return $"""
            You are a research assistant answering questions about a single academic paper.
            Answer ONLY using the excerpts below. Cite page numbers like (p.N) inline where relevant.
            If the excerpts don't contain the answer, say you don't know based on the paper — do not
            use outside knowledge.

            Excerpts:
            {excerpts}
            """;
    }
}
