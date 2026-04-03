namespace Ananke.Orchestration.Knowledge;

/// <summary>Search mode for knowledge store queries.</summary>
public enum SearchMode
{
    /// <summary>Dense vector similarity search (cosine / dot-product).</summary>
    Dense,

    /// <summary>Sparse keyword search (BM25 / TF-IDF).</summary>
    Sparse,

    /// <summary>Hybrid search combining dense and sparse results via reciprocal rank fusion.</summary>
    Hybrid
}

/// <summary>A document to be embedded and stored in the knowledge store.</summary>
public sealed record KnowledgeDocument
{
    /// <summary>Unique identifier for the document. Used for upsert deduplication.</summary>
    public required string Id { get; init; }

    /// <summary>The text content to embed and index.</summary>
    public required string Text { get; init; }

    /// <summary>Arbitrary metadata stored alongside the vector (source, page, tags, etc.).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>A search result chunk returned from the knowledge store.</summary>
public sealed record KnowledgeChunk
{
    /// <summary>The document ID this chunk originated from.</summary>
    public required string Id { get; init; }

    /// <summary>The original text content of the chunk.</summary>
    public required string Text { get; init; }

    /// <summary>Similarity score (higher is more relevant). Scale depends on the implementation.</summary>
    public required float Score { get; init; }

    /// <summary>Metadata associated with this chunk.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Metadata filter for knowledge store operations. Each entry is a key-value pair
/// that must match exactly on the stored document metadata.
/// </summary>
public sealed class KnowledgeFilter : Dictionary<string, string>;

/// <summary>Options controlling knowledge store search behavior.</summary>
public sealed record SearchOptions
{
    /// <summary>Maximum number of results to return. Default is 5.</summary>
    public int TopK { get; init; } = 5;

    /// <summary>Minimum similarity score threshold. Results below this are excluded. Default is 0.</summary>
    public float ScoreThreshold { get; init; }

    /// <summary>Optional metadata filter applied before vector search.</summary>
    public KnowledgeFilter? Filter { get; init; }

    /// <summary>Search strategy. Default is <see cref="SearchMode.Dense"/>.</summary>
    public SearchMode Mode { get; init; } = SearchMode.Dense;
}

/// <summary>
/// Controls how search results are formatted when surfaced to agents via
/// <see cref="Tools.KnowledgeSearchTool"/> or <see cref="Tools.KnowledgeTools"/>.
/// </summary>
public sealed record SearchResultFormatting
{
    /// <summary>
    /// Include the source URI (<c>source_uri</c> metadata) in formatted results for
    /// citation and transparency. When <see langword="true"/> and <c>source_uri</c> is
    /// present, the URI is shown; otherwise falls back to the <c>source</c> dedup key.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool IncludeSourceUri { get; init; } = true;

    /// <summary>
    /// Include the page number (<c>page</c> metadata) in formatted results.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool IncludePage { get; init; } = true;
}
