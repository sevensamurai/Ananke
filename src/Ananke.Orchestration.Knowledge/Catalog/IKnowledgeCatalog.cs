namespace Ananke.Orchestration.Knowledge.Catalog;

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
    /// Retrieves the catalog entries for several sources at once. A source with no entry is left out, and each entry
    /// comes once however often its source is named; the order is not defined. Each entry's
    /// <see cref="CatalogEntry.Source"/> is its source as named (ordinal), since callers match entries to sources by it.
    /// A blank source is rejected, as <see cref="GetAsync"/> rejects it.
    /// </summary>
    /// <remarks>
    /// The default calls <see cref="GetAsync"/> once per source. The built-in catalogs answer in one query.
    /// </remarks>
    async Task<IReadOnlyList<CatalogEntry>> GetManyAsync(IEnumerable<string> sources, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var entries = new List<CatalogEntry>();
        foreach (var source in sources.Distinct(StringComparer.Ordinal))
        {
            if (await GetAsync(source, ct).ConfigureAwait(false) is { } entry)
                entries.Add(entry);
        }
        return entries;
    }

    /// <summary>
    /// Retrieves the entries whose <see cref="CatalogEntry.Replaces"/> names <paramref name="source"/> (ordinal), superseded
    /// ones included; the order is not defined. A blank source is rejected, as <see cref="GetAsync"/> rejects it.
    /// </summary>
    /// <remarks>
    /// The default browses every entry (<see cref="BrowseAsync"/> with no limit) and keeps those that name the source, so
    /// over a catalog whose browsing leaves entries out it finds only those listed. The built-in catalogs answer in one
    /// query.
    /// </remarks>
    async Task<IReadOnlyList<CatalogEntry>> GetReplacingAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var entries = await BrowseAsync(new CatalogBrowseOptions { Limit = int.MaxValue }, ct).ConfigureAwait(false);
        return [.. entries.Where(e => e.Replaces.Contains(source, StringComparer.Ordinal))];
    }

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
