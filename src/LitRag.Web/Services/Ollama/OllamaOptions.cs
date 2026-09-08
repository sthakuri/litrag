namespace LitRag.Web.Services.Ollama;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "llama3.1:8b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Timeout for a single non-streaming request (validation, embeddings).</summary>
    public int RequestTimeoutSeconds { get; set; } = 60;
}
