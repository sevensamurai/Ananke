using System.Diagnostics.CodeAnalysis;

namespace Ananke.Postgres;

/// <summary>Where a Postgres knowledge store or catalog keeps its rows, and how it reads text for keyword search.</summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record PostgresKnowledgeOptions
{
    /// <summary>
    /// The table the rows go in, created on first use. Lower-case letters, digits and underscores, starting with a
    /// letter or underscore; it may be qualified with a schema (<c>knowledge.chunks</c>).
    /// </summary>
    public string Table { get; init; } = "ananke_knowledge";

    /// <summary>
    /// The Postgres text search configuration keyword search reads text with. Default is <c>simple</c>, which lower-cases
    /// and keeps words as they are in any language; <c>english</c> also stems and drops stop words.
    /// </summary>
    public string TextSearchConfiguration { get; init; } = "simple";

    /// <summary>
    /// The length of the embedder's vectors. When <see langword="null"/>, it is taken from the embedder's identity, or
    /// else from one embedding made when the table is first used.
    /// </summary>
    public int? Dimensions { get; init; }
}
