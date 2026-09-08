using System.Text.Json;
using System.Text.Json.Serialization;
using LitRag.Web.Services.Ollama;
using LitRag.Web.Services.Pdf;

namespace LitRag.Web.Services.Validation;

public record PaperValidationResult(
    bool IsValid,
    string Reason,
    string? Title,
    string? Authors,
    string? Venue,
    int? Year);

public class PaperValidationService(OllamaClient ollama, ILogger<PaperValidationService> logger)
{
    private const string SystemPrompt = """
        You are a strict classifier that decides whether a document excerpt comes from a published
        academic or scientific research paper (e.g. a journal article, conference paper, or preprint).
        Slide decks, invoices, resumes, book chapters without citations, manuals, and blog posts are NOT
        research papers. Respond with ONLY a JSON object, no other text, matching exactly this shape:
        {"isResearchPaper": bool, "reason": "short explanation", "title": "string or null",
         "authors": "comma-separated names or null", "venue": "journal/conference name or null",
         "year": number or null}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PaperValidationResult> ValidateAsync(ExtractedPdf pdf, CancellationToken ct = default)
    {
        if (pdf.PageCount == 0)
        {
            return new PaperValidationResult(false, "The PDF has no pages.", null, null, null, null);
        }

        if (pdf.TotalCharacters < 500)
        {
            return new PaperValidationResult(
                false,
                "This PDF doesn't contain much selectable text — it may be a scanned image. " +
                "LitRAG needs a text-based PDF to validate, index, and chat about it.",
                null, null, null, null);
        }

        var lowerText = pdf.FullText.ToLowerInvariant();
        var hasAbstract = lowerText.Contains("abstract");
        var hasReferences = lowerText.Contains("references") || lowerText.Contains("bibliography");

        string rawResponse;
        try
        {
            var excerpt = BuildExcerpt(pdf);
            var userPrompt =
                $"Document has {pdf.PageCount} page(s). Heuristic scan found an \"abstract\" section: {hasAbstract}; " +
                $"a references/bibliography section: {hasReferences}.\n\nExcerpt:\n{excerpt}";
            rawResponse = await ollama.GenerateJsonAsync(SystemPrompt, userPrompt, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ollama classification call failed; falling back to structural heuristics.");
            return HeuristicFallback(hasAbstract, hasReferences, aiUnavailable: true);
        }

        var classification = TryParse(rawResponse);
        if (classification is null)
        {
            logger.LogWarning("Could not parse Ollama classification response: {Raw}", rawResponse);
            return HeuristicFallback(hasAbstract, hasReferences, aiUnavailable: true);
        }

        return new PaperValidationResult(
            classification.IsResearchPaper,
            classification.Reason ?? (classification.IsResearchPaper
                ? "Looks like a published research paper."
                : "Doesn't look like a published research paper."),
            classification.Title,
            classification.Authors,
            classification.Venue,
            classification.Year);
    }

    private static PaperValidationResult HeuristicFallback(bool hasAbstract, bool hasReferences, bool aiUnavailable)
    {
        var prefix = aiUnavailable ? "AI validation is unavailable (is Ollama running?). " : string.Empty;

        if (hasAbstract && hasReferences)
        {
            return new PaperValidationResult(
                true,
                prefix + "Accepted based on document structure: found an abstract and a references section.",
                null, null, null, null);
        }

        return new PaperValidationResult(
            false,
            prefix + "Could not confirm this is a research paper — no clear abstract and references " +
            "section were found in the document.",
            null, null, null, null);
    }

    private static string BuildExcerpt(ExtractedPdf pdf)
    {
        const int maxChars = 6000;
        var head = string.Join("\n", pdf.Pages.Take(2).Select(p => p.Text));
        var tail = pdf.Pages.Count > 2 ? pdf.Pages[^1].Text : string.Empty;

        var combined = tail.Length > 0 ? $"{head}\n\n[...]\n\n{tail}" : head;
        return combined.Length > maxChars ? combined[..maxChars] : combined;
    }

    private static ClassificationResponse? TryParse(string raw)
    {
        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ClassificationResponse>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Some local models wrap JSON in prose or code fences despite instructions; extract the first {...} block.</summary>
    private static string? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }

    private record ClassificationResponse(
        [property: JsonPropertyName("isResearchPaper")] bool IsResearchPaper,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("authors")] string? Authors,
        [property: JsonPropertyName("venue")] string? Venue,
        [property: JsonPropertyName("year")] int? Year);
}
