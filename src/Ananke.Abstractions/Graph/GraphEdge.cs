namespace Ananke.Abstractions.Graph;

/// <summary>
/// An immutable, typed, provenance-tagged directed edge in a <see cref="IKnowledgeGraph"/>.
/// </summary>
public sealed record GraphEdge
{
    /// <summary>ID of the source node.</summary>
    public required string FromId { get; init; }

    /// <summary>ID of the target node.</summary>
    public required string ToId { get; init; }

    /// <summary>Semantic relation label (e.g. <c>tagged</c>, <c>co_occurs</c>, <c>follows</c>).</summary>
    public required string Relation { get; init; }

    /// <summary>How this edge was established.</summary>
    public required EdgeProvenance Provenance { get; init; }

    /// <summary>Relative weight; defaults to <c>1</c>.</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>Wall-clock time when the edge was first observed or inferred.</summary>
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Optional key/value metadata attached to this edge.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>();
}
