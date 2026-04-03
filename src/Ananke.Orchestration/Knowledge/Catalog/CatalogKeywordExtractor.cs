using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;

namespace Ananke.Orchestration.Knowledge.Catalog;

/// <summary>
/// Uses an LLM to extract keywords, a category label, and a summary from document text.
/// The result is used to build <see cref="CatalogEntry"/> instances for the knowledge catalog.
/// </summary>
public sealed class CatalogKeywordExtractor
{
    private const string ExtractionPrompt =
        """
        You are a librarian cataloging a document. Given the text below, extract:
        1. keywords — 5 to 10 descriptive keywords or key phrases that capture the main topics.
        2. category — a single broad category label (e.g. "software-engineering", "data-science", "policy", "finance").
        3. summary  — a concise 1–2 sentence summary of the document's content and domain.

        Focus on specific subjects, techniques, and domain. Do NOT use generic descriptions.
        Respond in the specified JSON format.
        """;

    private static readonly string ResponseSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            keywords = new
            {
                type = "array",
                items = new { type = "string" },
                description = "5-10 descriptive keywords or key phrases"
            },
            category = new
            {
                type = "string",
                description = "Single broad category label"
            },
            summary = new
            {
                type = "string",
                description = "Concise 1-2 sentence summary"
            }
        },
        required = new[] { "keywords", "category", "summary" },
        additionalProperties = false
    });

    private readonly IAgentModel _model;
    private readonly int _maxTextLength;

    /// <summary>
    /// Creates a keyword extractor backed by the specified LLM.
    /// </summary>
    /// <param name="model">Agent model used for extraction (structured output preferred).</param>
    /// <param name="maxTextLength">
    /// Maximum number of characters sent to the model. Longer texts are truncated.
    /// Default is <c>4000</c>.
    /// </param>
    public CatalogKeywordExtractor(IAgentModel model, int maxTextLength = 4000)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _maxTextLength = maxTextLength;
    }

    /// <summary>
    /// Extracts keywords, category, and summary from the given document text.
    /// </summary>
    public async Task<CatalogEnrichment> ExtractAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var truncated = text.Length > _maxTextLength ? text[.._maxTextLength] : text;

        var request = new AgentRequest
        {
            SystemPrompt = ExtractionPrompt,
            Messages = [AgentMessage.User(truncated)],
            ResponseFormat = new AgentResponseFormat("catalog_enrichment", ResponseSchema)
        };

        var response = await _model.GenerateAsync(request, ct);
        return ParseResponse(response.Text ?? "{}");
    }

    private static CatalogEnrichment ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var keywords = root.TryGetProperty("keywords", out var kw)
                           && kw.ValueKind == JsonValueKind.Array
                ? kw.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList()
                : [];

            var category = root.TryGetProperty("category", out var cat)
                ? cat.GetString() ?? string.Empty
                : string.Empty;

            var summary = root.TryGetProperty("summary", out var sum)
                ? sum.GetString() ?? string.Empty
                : string.Empty;

            return new CatalogEnrichment
            {
                Keywords = keywords,
                Category = category,
                Summary = summary
            };
        }
        catch (JsonException)
        {
            // Graceful fallback when the model returns non-JSON
            return new CatalogEnrichment
            {
                Keywords = [],
                Category = string.Empty,
                Summary = json.Length > 200 ? json[..200] : json
            };
        }
    }
}
