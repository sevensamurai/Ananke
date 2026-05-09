namespace Ananke.Abstractions.Graph;

/// <summary>
/// Describes how a <see cref="GraphEdge"/> was established.
/// </summary>
public enum EdgeProvenance
{
    /// <summary>Asserted directly from a data source (e.g. an explicit tag assignment).</summary>
    Extracted,

    /// <summary>Derived from statistics or structural patterns (e.g. tag co-occurrence).</summary>
    Inferred,

    /// <summary>Produced by a heuristic or LLM with uncertain confidence.</summary>
    Ambiguous,
}
