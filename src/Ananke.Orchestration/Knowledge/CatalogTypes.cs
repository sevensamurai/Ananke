namespace Ananke.Orchestration.Knowledge;

/// <summary>Time-decay function applied to catalog-aware search results.</summary>
public enum TimeDecayFunction
{
    /// <summary>Exponential decay: weight = e^(-ln2 × age / halfLife). Smooth, gradual drop-off.</summary>
    Exponential,

    /// <summary>Linear decay: weight = max(0, 1 − age / (2 × halfLife)). Reaches zero at twice the half-life.</summary>
    Linear
}

/// <summary>
/// Controls how document age affects search result ranking in <see cref="CatalogAwareKnowledgeStore"/>.
/// Older documents receive a lower weight multiplied against their vector similarity score,
/// so fresher content ranks higher when relevance is similar.
/// </summary>
public sealed record TimeDecayOptions
{
    /// <summary>Decay function shape. Default is <see cref="TimeDecayFunction.Exponential"/>.</summary>
    public TimeDecayFunction Function { get; init; } = TimeDecayFunction.Exponential;

    /// <summary>
    /// Number of days until a document's weight drops to 50 % of its original score.
    /// Default is <c>90</c> days.
    /// </summary>
    public double HalfLifeDays { get; init; } = 90;

    /// <summary>
    /// Minimum weight floor — documents are never penalized below this fraction.
    /// Ensures old-but-unique knowledge never disappears entirely.
    /// Default is <c>0.3</c> (30 %).
    /// </summary>
    public float FloorWeight { get; init; } = 0.3f;
}

/// <summary>A document-level entry in the knowledge catalog.</summary>
public sealed record CatalogEntry
{
    /// <summary>Source identifier (URL, file path, or stable unique ID) matching chunk metadata.</summary>
    public required string Source { get; init; }

    /// <summary>LLM-generated or manual summary of the document's content and domain.</summary>
    public required string Summary { get; init; }

    /// <summary>Descriptive keywords or key phrases extracted from the document.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>Broad category label (e.g. "software-engineering", "policy", "finance").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Timestamp when this document was last indexed.</summary>
    public required DateTimeOffset IndexedAt { get; init; }

    /// <summary>Number of chunks stored in the knowledge store for this document.</summary>
    public required int ChunkCount { get; init; }

    /// <summary>
    /// If set, indicates this document has been superseded by a newer version.
    /// Superseded entries are excluded from <see cref="IKnowledgeCatalog.DiscoverAsync"/> results.
    /// </summary>
    public string? SupersededBy { get; init; }
}

/// <summary>A catalog search result pairing a <see cref="CatalogEntry"/> with a relevance score.</summary>
public sealed record CatalogSearchResult
{
    /// <summary>The matched catalog entry.</summary>
    public required CatalogEntry Entry { get; init; }

    /// <summary>Semantic similarity score (higher is more relevant).</summary>
    public required float Score { get; init; }
}

/// <summary>Options for browsing catalog entries.</summary>
public sealed record CatalogBrowseOptions
{
    /// <summary>Optional category filter. Only entries matching this category are returned.</summary>
    public string? Category { get; init; }

    /// <summary>Optional recency filter. Entries older than this are excluded.</summary>
    public DateTimeOffset? NotOlderThan { get; init; }

    /// <summary>Maximum number of entries to return. Default is <c>50</c>.</summary>
    public int Limit { get; init; } = 50;
}

/// <summary>LLM-extracted enrichment data for a catalog entry.</summary>
public sealed record CatalogEnrichment
{
    /// <summary>Descriptive keywords or key phrases.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>Broad category label.</summary>
    public required string Category { get; init; }

    /// <summary>Concise summary of the document's content and domain.</summary>
    public required string Summary { get; init; }
}
