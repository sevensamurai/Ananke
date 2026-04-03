namespace Ananke.Orchestration.Knowledge.Linking;

/// <summary>
/// A directed, weighted link between two knowledge chunks. Links represent
/// relationships discovered during post-ingestion analysis (e.g. "references",
/// "extends", "prerequisite", "example-of").
/// </summary>
public sealed record DocumentLink
{
    /// <summary>The chunk ID where the link originates.</summary>
    public required string SourceId { get; init; }

    /// <summary>The chunk ID the link points to.</summary>
    public required string TargetId { get; init; }

    /// <summary>
    /// LLM-classified relationship type (e.g. "references", "contradicts",
    /// "extends", "prerequisite", "example-of").
    /// </summary>
    public required string Relationship { get; init; }

    /// <summary>Link strength in <c>[0, 1]</c>. Default is <c>1.0</c>.</summary>
    public float Weight { get; init; } = 1.0f;
}

/// <summary>
/// Persistent graph of cross-document links between knowledge chunks.
/// Implementations store directed, weighted edges independently of the
/// vector store so that link maintenance does not require re-embedding.
/// </summary>
/// <remarks>
/// The link graph is a separate concern from the vector store. It is queried
/// during search to expand results via graph traversal (spreading activation)
/// and maintained during ingestion by <see cref="DocumentLinkExtractor"/>.
/// </remarks>
public interface IDocumentLinkGraph
{
    /// <summary>
    /// Adds a directed link from <paramref name="sourceChunkId"/> to
    /// <paramref name="targetChunkId"/> with an LLM-classified relationship type.
    /// If a link between the same source and target already exists, it is replaced.
    /// </summary>
    Task AddLinkAsync(
        string sourceChunkId,
        string targetChunkId,
        string relationship,
        float weight = 1.0f,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all links reachable from <paramref name="chunkId"/> within
    /// <paramref name="maxHops"/> traversal steps.
    /// </summary>
    Task<IReadOnlyList<DocumentLink>> GetLinksAsync(
        string chunkId, int maxHops = 1, CancellationToken ct = default);

    /// <summary>
    /// Removes all links where <paramref name="chunkId"/> is either the source or target.
    /// Called during document deletion to keep the graph consistent.
    /// </summary>
    Task RemoveLinksAsync(string chunkId, CancellationToken ct = default);
}

/// <summary>
/// Options controlling how <see cref="LinkedKnowledgeStore"/> expands search
/// results through the document link graph.
/// </summary>
public sealed record LinkedSearchOptions
{
    /// <summary>
    /// Whether to expand search results through graph traversal.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool ExpandGraph { get; init; } = true;

    /// <summary>
    /// Number of top vector-search results used as seeds for graph expansion.
    /// Default is <c>3</c>.
    /// </summary>
    public int ExpansionSeeds { get; init; } = 3;

    /// <summary>
    /// Maximum number of hops to traverse from each seed chunk.
    /// Default is <c>1</c>.
    /// </summary>
    public int MaxHops { get; init; } = 1;

    /// <summary>
    /// Discount factor applied to graph-expanded results. The score of a linked chunk
    /// is <c>seedScore × linkWeight × GraphScoreDiscount</c>, ensuring graph results
    /// rank below their seed unless the seed's score is very high.
    /// Default is <c>0.8</c>.
    /// </summary>
    public float GraphScoreDiscount { get; init; } = 0.8f;
}
