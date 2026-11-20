using System.Diagnostics.CodeAnalysis;
using System.Text;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Knowledge.Catalog;

/// <summary>
/// Factory for creating a <see cref="ToolKit"/> with catalog browsing and discovery tools.
/// These tools give agents awareness of what documents exist in the knowledge base,
/// enabling two-phase search: discover relevant sources → deep-search within them.
/// </summary>
public static class KnowledgeCatalogTools
{
    /// <summary>How many documents <c>list_documents</c> shows at most, the newest.</summary>
    private const int MaxListedDocuments = 200;

    /// <summary>
    /// Creates a <see cref="ToolKit"/> with <c>browse_catalog</c>, <c>discover_sources</c> and <c>list_documents</c>
    /// tools. <c>list_documents</c> lists every document whose header field has a value (every plan, with its status),
    /// newest first by the document's own date, from the fields ingestion kept on each entry.
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
                paramDescription: "Natural language query describing the topic to find relevant sources for")
            .AddTool(
                "list_documents",
                "List every document whose header field has a value, such as every document of a type, newest first, " +
                "with its date, its header fields and what supersedes it. Use this for a roundup (the status of every " +
                "plan) rather than a search, which returns only the closest few.",
                tool => tool
                    .Param("field", "The header field to match, such as 'Type' or 'Status'")
                    .Param("value", "The value it must have, such as 'plan'; compared without case")
                    .OnExecute(async (args, ct) =>
                    {
                        var entries = await catalog.BrowseAsync(new CatalogBrowseOptions
                        {
                            Field = args.Get("field"),
                            Value = args.Get("value"),
                            Order = CatalogBrowseOrder.EffectiveAt,
                            // One more than is shown, to tell whether more match.
                            Limit = MaxListedDocuments + 1,
                        }, ct);
                        return FormatListResults(entries);
                    }));
    }

    /// <summary>
    /// Creates a <see cref="ToolKit"/> with <c>list_linked_documents</c>: given a document's source id, the documents it
    /// links to, newest first, each with its date and what supersedes it, read from the links
    /// <see cref="DocumentProcessor.Hyperlinks"/> stored, with no model call.
    /// </summary>
    /// <param name="reader">Reads the stored links, and the catalog when it was given one.</param>
    /// <param name="name">Name for the returned <see cref="ToolKit"/>. Default is <c>"document_links"</c>.</param>
    [Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
    public static ToolKit CreateLinks(LinkedDocumentReader reader, string name = "document_links")
    {
        ArgumentNullException.ThrowIfNull(reader);

        return new ToolKit(name).AddTool(
            "list_linked_documents",
            "List the documents a document links to, newest first, with each one's date and what supersedes it. Use " +
            "this when a question asks what a document builds on, cites or follows from.",
            tool => tool
                .Param("source", "The source id of the document whose links to list, as search results name it")
                .OnExecute(async (args, ct) =>
                {
                    var source = args.Get("source");
                    return FormatLinkedDocuments(source, await reader.ListAsync(source, ct: ct));
                }));
    }

#pragma warning disable ANNKEEXP004 // LinkedDocument is experimental, as is the tool that lists it.
    private static string FormatLinkedDocuments(string source, IReadOnlyList<LinkedDocument> linked)
    {
        if (linked.Count == 0)
            return $"No links from {source}.";

        var sb = new StringBuilder();
        sb.AppendLine($"{source} links to {linked.Count} document(s), newest first:");
        foreach (var document in linked)
        {
            sb.Append($"• {document.Source}");
            if ((document.EffectiveAt ?? document.IndexedAt) is { } date)
                sb.Append($" — {date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}");
            if (document.SupersededBy is not null)
                sb.Append($" — Superseded by: {document.SupersededBy}");
            if (!document.Indexed)
                sb.Append(" — not indexed");
            sb.AppendLine();
        }

        return sb.ToString();
    }
#pragma warning restore ANNKEEXP004

    private static string FormatListResults(IReadOnlyList<CatalogEntry> entries)
    {
        if (entries.Count == 0)
            return "No document has that field and value.";

        var sb = new StringBuilder();
        sb.AppendLine(entries.Count > MaxListedDocuments
            ? $"More than {MaxListedDocuments} documents match; the newest {MaxListedDocuments}:"
            : $"{entries.Count} document(s), newest first:");
        foreach (var entry in entries.Take(MaxListedDocuments))
        {
            var date = (entry.EffectiveAt ?? entry.IndexedAt).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var fields = string.Join(" · ", entry.Fields.Select(f => $"{f.Key}: {f.Value}"));
            sb.Append($"• {entry.Source} — {date}");
            if (fields.Length > 0)
                sb.Append($" — {fields}");
            if (entry.SupersededBy is not null)
                sb.Append($" — Superseded by: {entry.SupersededBy}");
            sb.AppendLine();
        }

        return sb.ToString();
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
