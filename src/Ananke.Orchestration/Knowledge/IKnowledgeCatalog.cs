namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Document-level catalog for the knowledge store. Maintains one entry per source document
/// with LLM-enriched metadata (summary, keywords, category, timestamp) to enable
/// two-phase discovery: agents first discover relevant sources via the catalog, then
/// deep-search within those sources for specific chunks.
/// </summary>
/// <remarks>
/// <para>
/// Built-in implementations: <see cref="InMemoryKnowledgeCatalog"/> (tests / single-process)
/// and <c>QdrantKnowledgeCatalog</c> (distributed, in the <c>Ananke.Qdrant</c> package).
/// </para>
/// <para>
/// Wire into the ingestion pipeline via <see cref="CatalogAwareKnowledgeStore"/>, which
/// decorates any <see cref="IKnowledgeStore"/> and automatically updates the catalog on
/// upsert and delete operations.
/// </para>
/// </remarks>
public interface IKnowledgeCatalog
{
    /// <summary>
    /// Indexes or updates a catalog entry for a document source.
    /// If an entry for the same <see cref="CatalogEntry.Source"/> already exists, it is replaced.
    /// </summary>
    Task IndexAsync(CatalogEntry entry, CancellationToken ct = default);

    /// <summary>Removes the catalog entry for the specified source.</summary>
    Task RemoveAsync(string source, CancellationToken ct = default);

    /// <summary>Retrieves the catalog entry for a specific source, or <see langword="null"/> if not found.</summary>
    Task<CatalogEntry?> GetAsync(string source, CancellationToken ct = default);

    /// <summary>
    /// Semantic search over catalog summaries and keywords. Returns document-level matches
    /// ranked by relevance. Superseded entries are excluded.
    /// </summary>
    /// <param name="query">Natural language query.</param>
    /// <param name="topK">Maximum number of results. Default is <c>5</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CatalogSearchResult>> DiscoverAsync(
        string query, int topK = 5, CancellationToken ct = default);

    /// <summary>
    /// Lists catalog entries, optionally filtered by category and/or recency.
    /// Results are ordered by <see cref="CatalogEntry.IndexedAt"/> descending.
    /// </summary>
    Task<IReadOnlyList<CatalogEntry>> BrowseAsync(
        CatalogBrowseOptions? options = null, CancellationToken ct = default);
}
