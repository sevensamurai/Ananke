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

    /// <summary>Optional key/value metadata attached to this node.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>();
}
