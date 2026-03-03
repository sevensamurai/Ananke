using System.Text;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Factory for creating a <see cref="ToolKit"/> with catalog browsing and discovery tools.
/// These tools give agents awareness of what documents exist in the knowledge base,
/// enabling two-phase search: discover relevant sources → deep-search within them.
/// </summary>
public static class KnowledgeCatalogTools
{
    /// <summary>
    /// Creates a <see cref="ToolKit"/> with <c>browse_catalog</c> and <c>discover_sources</c> tools.
    /// </summary>
    /// <param name="catalog">The knowledge catalog to expose to agents.</param>
    /// <param name="name">Name for the returned <see cref="ToolKit"/>. Default is <c>"knowledge_catalog"</c>.</param>
    /// <param name="browseDescription">Description for the browse tool.</param>
    /// <param name="discoverDescription">Description for the discover tool.</param>
    public static ToolKit Create(
        IKnowledgeCatalog catalog,
        string name = "knowledge_catalog",
        string? browseDescription = null,
        string? discoverDescription = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        browseDescription ??=
            "Browse the knowledge catalog to see what documents and topics are available " +
            "in the knowledge base. Optionally filter by category.";

        discoverDescription ??=
            "Search the knowledge catalog to discover which document sources are most " +
            "relevant to a topic. Returns document-level summaries with keywords and " +
            "categories, not individual text chunks. Use this to decide which sources " +
            "to deep-search.";

        return new ToolKit(name)
            .AddTool(
                name: "browse_catalog",
                description: browseDescription,
                execute: async category =>
                {
                    var options = string.IsNullOrWhiteSpace(category)
                        ? null
                        : new CatalogBrowseOptions { Category = category };

                    var entries = await catalog.BrowseAsync(options);
                    return FormatBrowseResults(entries);
                },
                paramName: "category",
                paramDescription:
                    "Optional category filter (e.g. 'software-engineering'). " +
                    "Pass empty string to browse all categories.")
            .AddTool(
                name: "discover_sources",
                description: discoverDescription,
                execute: async query =>
                {
                    var results = await catalog.DiscoverAsync(query);
                    return FormatDiscoverResults(results);
                },
                paramName: "query",
                paramDescription: "Natural language query describing the topic to find relevant sources for");
    }

    private static string FormatBrowseResults(IReadOnlyList<CatalogEntry> entries)
    {
        if (entries.Count == 0)
            return "The knowledge catalog is empty — no documents have been indexed.";

        var sb = new StringBuilder();
        sb.AppendLine($"Knowledge catalog contains {entries.Count} document(s):");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            sb.AppendLine($"• [{entry.Category}] {entry.Source}");

            if (entry.Summary.Length > 0)
                sb.AppendLine($"  {entry.Summary}");
            if (entry.Keywords.Count > 0)
                sb.AppendLine($"  Keywords: {string.Join(", ", entry.Keywords)}");

            sb.AppendLine($"  Indexed: {entry.IndexedAt:yyyy-MM-dd HH:mm} UTC | Chunks: {entry.ChunkCount}");

            if (entry.SupersededBy is not null)
                sb.AppendLine($"  ⚠ Superseded by: {entry.SupersededBy}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatDiscoverResults(IReadOnlyList<CatalogSearchResult> results)
    {
        if (results.Count == 0)
            return "No relevant document sources found in the catalog.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} relevant source(s):");
        sb.AppendLine();

        foreach (var result in results)
        {
            var entry = result.Entry;
            sb.AppendLine($"--- {entry.Source} (relevance: {result.Score:F3}) ---");

            if (entry.Summary.Length > 0)
                sb.AppendLine(entry.Summary);
            if (entry.Keywords.Count > 0)
                sb.AppendLine($"Keywords: {string.Join(", ", entry.Keywords)}");

            sb.AppendLine(
                $"Category: {entry.Category} | " +
                $"Indexed: {entry.IndexedAt:yyyy-MM-dd HH:mm} UTC | " +
                $"Chunks: {entry.ChunkCount}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
