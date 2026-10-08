using LitRag.Web.Data;
using LitRag.Web.Data.Entities;
using LitRag.Web.Services.Pdf;
using LitRag.Web.Services.Rag;
using Microsoft.EntityFrameworkCore;

namespace LitRag.Web.Services.Library;

public record UploadPaperRequest(
    string OriginalFileName,
    byte[] FileBytes,
    string Title,
    string? Authors,
    string? Venue,
    int? Year,
    string? SourceUrl,
    IReadOnlyList<string> Tags);

public record SaveResult(Paper Paper, bool Indexed, string? IndexError);
public record IndexResult(bool Indexed, string? Error);

public class LibraryService(
    LitRagDbContext db,
    IWebHostEnvironment env,
    PdfTextExtractor pdfExtractor,
    TextChunker chunker,
    EmbeddingService embeddingService,
    ILogger<LibraryService> logger)
{
    private string LibraryFolder => Path.Combine(env.WebRootPath, "library");

    public async Task<SaveResult> SaveAsync(
        UploadPaperRequest request,
        ExtractedPdf extractedPdf,
        IProgress<EmbeddingProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(LibraryFolder);
        var storedFileName = $"{Guid.NewGuid():N}.pdf";
        var fullPath = Path.Combine(LibraryFolder, storedFileName);
        await File.WriteAllBytesAsync(fullPath, request.FileBytes, ct);

        var paper = new Paper
        {
            Title = request.Title,
            Authors = request.Authors,
            Venue = request.Venue,
            Year = request.Year,
            SourceUrl = request.SourceUrl,
            TagsCsv = Paper.TagsToCsv(request.Tags),
            FileName = request.OriginalFileName,
            StoredFileName = storedFileName,
            PageCount = extractedPdf.PageCount
        };

        db.Papers.Add(paper);
        await db.SaveChangesAsync(ct);

        var indexResult = await IndexAsync(paper.Id, extractedPdf, progress, ct);
        return new SaveResult(paper, indexResult.Indexed, indexResult.Error);
    }

    /// <summary>Re-runs chunking + embedding for an already-saved paper, e.g. after Ollama was unavailable at upload time.</summary>
    public async Task<IndexResult> ReindexAsync(
        int paperId, IProgress<EmbeddingProgress>? progress = null, CancellationToken ct = default)
    {
        var paper = await db.Papers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
        if (paper is null)
        {
            return new IndexResult(false, "Paper not found.");
        }

        var path = Path.Combine(LibraryFolder, paper.StoredFileName);
        if (!File.Exists(path))
        {
            return new IndexResult(false, "The stored PDF file is missing on disk.");
        }

        ExtractedPdf extracted;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            extracted = pdfExtractor.Extract(bytes);
        }
        catch (Exception ex)
        {
            return new IndexResult(false, $"Could not re-read the PDF: {ex.Message}");
        }

        var existingChunks = db.PaperChunks.Where(c => c.PaperId == paperId);
        db.PaperChunks.RemoveRange(existingChunks);
        await db.SaveChangesAsync(ct);

        return await IndexAsync(paperId, extracted, progress, ct);
    }

    private async Task<IndexResult> IndexAsync(
        int paperId, ExtractedPdf extractedPdf, IProgress<EmbeddingProgress>? progress, CancellationToken ct)
    {
        try
        {
            var textChunks = chunker.Chunk(extractedPdf);
            var chunkEntities = await embeddingService.EmbedChunksAsync(paperId, textChunks, progress, ct);
            db.PaperChunks.AddRange(chunkEntities);
            await db.SaveChangesAsync(ct);
            return new IndexResult(true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding failed for paper {PaperId}; paper saved without chat indexing.", paperId);
            return new IndexResult(false, ex.Message);
        }
    }

    public Task<List<Paper>> SearchAsync(string? query, CancellationToken ct = default)
    {
        var papersQuery = db.Papers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            papersQuery = papersQuery.Where(p =>
                EF.Functions.Like(p.Title, $"%{q}%") ||
                (p.Authors != null && EF.Functions.Like(p.Authors, $"%{q}%")) ||
                (p.Venue != null && EF.Functions.Like(p.Venue, $"%{q}%")) ||
                (p.TagsCsv != null && EF.Functions.Like(p.TagsCsv, $"%{q}%")));
        }

        return papersQuery.OrderByDescending(p => p.UploadedAtUtc).ToListAsync(ct);
    }

    public Task<Paper?> GetAsync(int id, CancellationToken ct = default) =>
        db.Papers.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<bool> UpdateReadingStatusAsync(int id, ReadingStatus status, CancellationToken ct = default)
    {
        var paper = await db.Papers.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (paper is null)
        {
            return false;
        }

        paper.ReadingStatus = status;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static string GetRelativeFileUrl(Paper paper) => $"/library/{paper.StoredFileName}";

    /// <summary>Paper IDs that have at least one indexed chunk (i.e. are ready for chat).</summary>
    public async Task<HashSet<int>> GetIndexedPaperIdsAsync(IEnumerable<int> paperIds, CancellationToken ct = default)
    {
        var ids = paperIds.ToList();
        var indexed = await db.PaperChunks
            .Where(c => ids.Contains(c.PaperId))
            .Select(c => c.PaperId)
            .Distinct()
            .ToListAsync(ct);
        return [.. indexed];
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var paper = await db.Papers.FindAsync([id], ct);
        if (paper is null)
        {
            return;
        }

        var path = Path.Combine(LibraryFolder, paper.StoredFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        db.Papers.Remove(paper);
        await db.SaveChangesAsync(ct);
    }
}
