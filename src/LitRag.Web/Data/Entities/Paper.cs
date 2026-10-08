namespace LitRag.Web.Data.Entities;

public enum ReadingStatus
{
    Unread,
    Reading,
    Completed
}

public class Paper
{
    public int Id { get; set; }

    public required string Title { get; set; }
    public string? Authors { get; set; }
    public string? Venue { get; set; }
    public int? Year { get; set; }
    public string? SourceUrl { get; set; }
    public string? TagsCsv { get; set; }

    public required string FileName { get; set; }
    public required string StoredFileName { get; set; }
    public int PageCount { get; set; }

    public ReadingStatus ReadingStatus { get; set; } = ReadingStatus.Unread;

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    public List<PaperChunk> Chunks { get; set; } = [];
    public List<ChatMessage> ChatMessages { get; set; } = [];

    public IReadOnlyList<string> Tags =>
        string.IsNullOrWhiteSpace(TagsCsv)
            ? []
            : TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string TagsToCsv(IEnumerable<string> tags) =>
        string.Join(",", tags.Select(t => t.Trim()).Where(t => t.Length > 0));
}
