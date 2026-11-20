using System.Globalization;
using System.Text;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Knowledge.Tools;

/// <summary>
/// Factory for creating a <see cref="ToolKit"/> with a knowledge search tool
/// backed by any <see cref="IKnowledgeStore"/>.
/// The description passed to <see cref="Create"/> is key to agent autonomy:
/// a good description tells the model what domain the knowledge base covers
/// so it can decide when to search without being explicitly instructed.
/// </summary>
public static class KnowledgeSearchTool
{
    /// <summary>
    /// Creates a <see cref="ToolKit"/> containing a knowledge search tool.
    /// </summary>
    /// <param name="name">Name for the returned <see cref="ToolKit"/>.</param>
    /// <param name="store">The knowledge store to search.</param>
    /// <param name="description">
    /// Description of what this knowledge base contains. This is surfaced to the model
    /// as the tool description, so it should clearly describe the domain — e.g.
    /// <c>"Search a knowledge base of software engineering best practices, refactoring
    /// techniques, and design patterns from indexed reference materials."</c>
    /// </param>
    /// <param name="toolName">
    /// Name for the search tool. Default is <c>"search_knowledge"</c>.
    /// Use a domain-specific name for clarity — e.g. <c>"search_engineering_docs"</c>.
    /// </param>
    /// <param name="defaultOptions">Optional default search options (topK, threshold, filter).</param>
    /// <param name="formatting">
    /// Controls which metadata fields (source URI, page number) appear in formatted results.
    /// Default shows all available metadata.
    /// </param>
    public static ToolKit Create(
        string name,
        IKnowledgeStore store,
        string? description = null,
        string toolName = "search_knowledge",
        SearchOptions? defaultOptions = null,
        SearchResultFormatting? formatting = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(store);

        description ??= "Search the knowledge base for information from previously indexed documents.";
        formatting ??= new SearchResultFormatting();

        return new ToolKit(name)
            .AddTool(
                name: toolName,
                description: description,
                execute: async query =>
                {
                    var results = await store.SearchAsync(query, defaultOptions);
                    return FormatResults(results, formatting);
                },
                paramName: "query",
                paramDescription: "Natural language search query");
    }

#pragma warning disable ANNKEEXP004 // the metadata keys DocumentHeaderOptions writes a document's date and superseding under
    private const string EffectiveAtKey = Documents.DocumentHeaderOptions.EffectiveAtKey;
    private const string SupersededByKey = Documents.DocumentHeaderOptions.SupersededByKey;
#pragma warning restore ANNKEEXP004

    /// <summary>The day of an ISO 8601 date and time, as <c>yyyy-MM-dd</c>; the text as it is when it doesn't parse.</summary>
    private static string Day(string date) =>
        DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date;

    /// <summary>
    /// The results as text for a model: each one's score, source, page, its document's date (<c>effective_at</c>) and
    /// what supersedes it (<c>superseded_by</c>) when its metadata has them, then its text.
    /// </summary>
    internal static string FormatResults(
        IReadOnlyList<KnowledgeChunk> chunks,
        SearchResultFormatting? formatting = null)
    {
        if (chunks.Count == 0)
            return "No relevant results found in the knowledge base.";

        formatting ??= new SearchResultFormatting();

        var sb = new StringBuilder();
        sb.AppendLine($"Found {chunks.Count} relevant result(s):");
        sb.AppendLine();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            sb.AppendLine($"--- Result {i + 1} (score: {chunk.Score:F3}) ---");

            if (formatting.IncludeSourceUri)
            {
                // Prefer source_uri (the original document location) over source (dedup key)
                if (chunk.Metadata.TryGetValue("source_uri", out var sourceUri))
                    sb.AppendLine($"Source: {sourceUri}");
                else if (chunk.Metadata.TryGetValue("source", out var source))
                    sb.AppendLine($"Source: {source}");
            }

            if (formatting.IncludePage && chunk.Metadata.TryGetValue("page", out var page))
                sb.AppendLine($"Page: {page}");

            if (chunk.Metadata.TryGetValue(EffectiveAtKey, out var date) && date.Length > 0)
                sb.AppendLine($"Date: {Day(date)}");

            if (chunk.Metadata.TryGetValue(SupersededByKey, out var supersededBy) && supersededBy.Length > 0)
                sb.AppendLine($"Superseded by: {supersededBy}");

            sb.AppendLine(chunk.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
