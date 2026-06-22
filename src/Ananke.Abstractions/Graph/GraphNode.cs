namespace Ananke.Abstractions.Graph;

/// <summary>
/// An immutable, typed node in a <see cref="IKnowledgeGraph"/>.
/// </summary>
public sealed record GraphNode
{
    /// <summary>Stable unique identifier for this node.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Semantic kind of the node (e.g. <c>entry</c>, <c>tag</c>, <c>episode</c>, <c>cell</c>).
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Additional labels beyond <see cref="Kind"/>. Optional; may or may not repeat
    /// <see cref="Kind"/>, in any order — producers should not rely on a specific position.
    /// Use <see cref="EffectiveLabels"/> for the normalized, Kind-first view.
    /// </summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>
    /// The full label set, normalized: <see cref="Kind"/> always first, followed by
    /// <see cref="Labels"/> deduplicated against it. This is the canonical view that
    /// storage backends and label-aware filters must use.
    /// </summary>
    public IReadOnlyList<string> EffectiveLabels =>
        Labels.Count == 0 ? [Kind] : [Kind, .. Labels.Where(l => l != Kind).Distinct()];

    /// <summary>Optional key/value metadata attached to this node.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>();
}
