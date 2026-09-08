namespace LitRag.Web.Data.Entities;

public enum ChatRole
{
    User,
    Assistant
}

public class ChatMessage
{
    public int Id { get; set; }

    public int PaperId { get; set; }
    public Paper? Paper { get; set; }

    public ChatRole Role { get; set; }
    public required string Content { get; set; }

    /// <summary>Comma-separated 1-based page numbers cited as sources (assistant messages only).</summary>
    public string? SourcePagesCsv { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
