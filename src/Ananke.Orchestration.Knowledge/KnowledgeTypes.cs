using System.Diagnostics.CodeAnalysis;

namespace Ananke.Orchestration.Knowledge;

/// <summary>Search mode for knowledge store queries.</summary>
/// <remarks>
/// A store that cannot search in a mode throws <see cref="NotSupportedException"/> rather than search in another.
/// <see cref="InMemoryKnowledgeStore"/> and the Postgres store support all three; the Qdrant store supports <see cref="Dense"/> only.
/// </remarks>
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
/// that must match exactly on the stored document metadata; each entry of <see cref="AnyOf"/> is a key whose stored
/// value must be one of several. A document matches when every entry of both holds.
/// </summary>
/// <remarks>
/// A store applies <see cref="AnyOf"/> wherever it applies the filter: in search, listing and delete. A filter whose
/// entries and <see cref="AnyOf"/> are both empty matches every document in search and listing; a delete refuses it.
/// The key <c>id</c>, as an entry or in <see cref="AnyOf"/>, is the chunk's own <see cref="KnowledgeChunk.Id"/>, not a
/// metadata key, on every store.
/// </remarks>
public sealed class KnowledgeFilter : Dictionary<string, string>
{
    /// <summary>
    /// Keys a document matches when its value is any of the values given, as in
    /// <c>new KnowledgeFilter { AnyOf = { ["outlet"] = ["TechCrunch", "The Verge"] } }</c>. A key with no values
    /// matches no document. A key both here and in the filter's own entries must match both.
    /// </summary>
    [Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
    public IDictionary<string, IReadOnlyList<string>> AnyOf { get; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>A new filter with the same entries and <see cref="AnyOf"/>: changing it leaves this one as it was.</summary>
    public KnowledgeFilter Copy()
    {
        var copy = new KnowledgeFilter();
        foreach (var (key, value) in this)
            copy[key] = value;
#pragma warning disable ANNKEEXP004 // copies AnyOf, experimental outside this assembly
        foreach (var (key, values) in AnyOf)
            copy.AnyOf[key] = values;
#pragma warning restore ANNKEEXP004
        return copy;
    }

    /// <summary>
    /// The values a document's <paramref name="key"/> may hold to match: the entry's value, or the values in
    /// <see cref="AnyOf"/>, or, when the key is in both, the entry's value only if it is among them. <see langword="null"/>
    /// when the filter doesn't limit the key.
    /// </summary>
    internal IReadOnlyList<string>? ValuesOf(string key)
    {
#pragma warning disable ANNKEEXP004 // reads AnyOf, experimental outside this assembly
        var anyOf = AnyOf.TryGetValue(key, out var values) ? values : null;
#pragma warning restore ANNKEEXP004
        if (TryGetValue(key, out var value))
            return anyOf is null || anyOf.Contains(value, StringComparer.Ordinal) ? [value] : [];
        return anyOf is null ? null : [.. anyOf.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="filter"/> has no entries and no <see cref="AnyOf"/>,
    /// which <see cref="IKnowledgeStore.DeleteAsync"/> refuses.
    /// </summary>
    internal static void ThrowIfEmptyForDelete(KnowledgeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
#pragma warning disable ANNKEEXP004 // reads AnyOf, experimental outside this assembly
        if (filter.Count == 0 && filter.AnyOf.Count == 0)
            throw new ArgumentException("A delete with an empty filter would remove every document; name what to delete.", nameof(filter));
#pragma warning restore ANNKEEXP004
    }
}

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

    /// <summary>
    /// In <see cref="SearchMode.Hybrid"/>, the weight of the dense ranking in the reciprocal rank fusion: a result at
    /// rank <c>r</c> (from 1) of that ranking adds <c>DenseWeight / (60 + r)</c> to its score; at 0 that ranking isn't
    /// searched. A finite number, not negative; both 0 is refused by <c>SearchAsync</c> with an
    /// <see cref="ArgumentException"/>. Default is 1.
    /// </summary>
    public float DenseWeight
    {
        get;
        init => field = Weight(value, nameof(DenseWeight));
    } = 1;

    /// <summary>
    /// In <see cref="SearchMode.Hybrid"/>, the weight of the keyword ranking in the reciprocal rank fusion, as
    /// <see cref="DenseWeight"/> is the dense ranking's; at 0 that ranking isn't searched. A finite number, not
    /// negative; both 0 is refused by <c>SearchAsync</c> with an <see cref="ArgumentException"/>. Default is 1.
    /// </summary>
    public float KeywordWeight
    {
        get;
        init => field = Weight(value, nameof(KeywordWeight));
    } = 1;

    /// <summary>
    /// At most this many results from one document (one <c>source</c>), so a question that needs several documents
    /// isn't answered from the first one alone. A store ranks more candidates to fill <see cref="TopK"/>. Default is no
    /// limit.
    /// </summary>
    public int? MaxPerSource { get; init; }

    /// <summary>
    /// With <see cref="MaxPerSource"/>, a document's chunks come back as one result, their texts joined in rank order,
    /// so <see cref="TopK"/> counts documents and each brings up to <see cref="MaxPerSource"/> passages. Default is
    /// <see langword="false"/>: each chunk is a result of its own.
    /// </summary>
    public bool JoinPerSource { get; init; }

    private static float Weight(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Must be a finite number, not negative.");
        return value;
    }
}

/// <summary>
/// Controls how search results are formatted when surfaced to agents via
/// <c>KnowledgeSearchTool</c> or <c>KnowledgeTools</c> (in <c>Ananke.Orchestration</c>).
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
