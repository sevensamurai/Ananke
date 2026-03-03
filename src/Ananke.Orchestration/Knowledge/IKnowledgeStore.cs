namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Vector-indexed knowledge store for semantic search over documents.
/// Implementations handle embedding storage, similarity search, and metadata filtering.
/// </summary>
/// <remarks>
/// Built-in implementations: <see cref="InMemoryKnowledgeStore"/> (tests / single-process)
/// and <c>QdrantKnowledgeStore</c> (distributed, in the <c>Ananke.Qdrant</c> package).
/// </remarks>
public interface IKnowledgeStore
{
    /// <summary>
    /// Searches the store for chunks semantically similar to <paramref name="query"/>.
    /// Returns results ranked by similarity score in descending order.
    /// </summary>
    Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Upserts documents into the store. Each document is embedded and indexed.
    /// Documents with existing IDs are overwritten.
    /// </summary>
    Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default);

    /// <summary>
    /// Deletes all documents matching the specified metadata filter.
    /// </summary>
    Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default);
}
