using System.Diagnostics.CodeAnalysis;

namespace Ananke.Postgres;

/// <summary>
/// Where a Postgres knowledge store or catalog keeps its rows, how it reads text for keyword search, and how its dense
/// search reads the HNSW index.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record PostgresKnowledgeOptions
{
    /// <summary>
    /// The table the rows go in, created on first use. Lower-case letters, digits and underscores, starting with a
    /// letter or underscore; it may be qualified with a schema (<c>knowledge.chunks</c>). At most 49 characters without
    /// the schema, and 52 for the schema and table together (without the dot), so the names derived from it fit Postgres's 63.
    /// </summary>
    public string Table { get; init; } = "ananke_knowledge";

    /// <summary>
    /// The Postgres text search configuration keyword search reads text with. Default is <c>simple</c>, which lower-cases
    /// and keeps words as they are in any language; <c>english</c> also stems and drops stop words.
    /// </summary>
    public string TextSearchConfiguration { get; init; } = "simple";

    /// <summary>
    /// Which rows keyword search scores. A query term is rare when it is found in at most this share of the table's
    /// rows, rounded up to whole rows, so on a small table a term in one row is rare. Only rows holding a rare query
    /// term are scored, by every query term they hold; a query with no rare term requires all its terms. When a filter
    /// keeps no such row, every term some kept row holds is required, and then any term is enough. Default is 0.05 (5%
    /// of rows); more than 0, at most 1. At 1 every term is rare, and every row holding any query term is scored.
    /// </summary>
    public double RareTermShare { get; init; } = 0.05;

    /// <summary>
    /// The length of the embedder's vectors. When <see langword="null"/>, it is taken from the embedder's identity, or
    /// else from one embedding made when the table is first used.
    /// </summary>
    public int? Dimensions { get; init; }

    /// <summary>
    /// The least <c>hnsw.ef_search</c> a dense search runs with: how many candidates pgvector reads from the HNSW index
    /// before the filter and the score threshold are applied. A search asking for more rows raises it to the rows
    /// asked, up to 1,000, pgvector's limit. Default is 40, pgvector's own default; 1 to 1,000.
    /// </summary>
    public int HnswEfSearch { get; init; } = 40;

    /// <summary>
    /// <c>hnsw.iterative_scan</c> for dense search, on pgvector 0.8 or later. With
    /// <see cref="PostgresIterativeScan.StrictOrder"/> (the default) a scan that has too few rows once the filter is
    /// applied reads on through the index until it has the rows asked, or has read <c>hnsw.max_scan_tuples</c>
    /// (20,000 by default). Ignored on an earlier pgvector, which reads <see cref="HnswEfSearch"/> candidates at most.
    /// </summary>
    public PostgresIterativeScan HnswIterativeScan { get; init; } = PostgresIterativeScan.StrictOrder;
}

/// <summary>The values of pgvector's <c>hnsw.iterative_scan</c> a dense search can run with.</summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public enum PostgresIterativeScan
{
    /// <summary><c>off</c>: the scan reads <see cref="PostgresKnowledgeOptions.HnswEfSearch"/> candidates at most.</summary>
    Off,

    /// <summary><c>strict_order</c>: the scan reads on until it has the rows asked, and returns them by distance.</summary>
    StrictOrder,
}
