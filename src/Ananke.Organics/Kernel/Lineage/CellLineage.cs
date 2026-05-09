namespace Ananke.Organics.Kernel.Lineage;

/// <summary>
/// Immutable record of a workflow cell's birth, ancestry, and eventual death.
/// Persisted by <see cref="ILineageStore"/>; survives the cell's removal.
/// </summary>
public sealed record CellLineage
{
    /// <summary>Unique identifier for this cell instance (typically same as <see cref="WorkflowName"/> for in-process hosts).</summary>
    public required string CellId { get; init; }

    /// <summary>Workflow name this cell was running.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Cell ID of the parent that divided to produce this cell. <see langword="null"/> for founder cells.</summary>
    public string? ParentCellId { get; init; }

    /// <summary>Generation counter. Founder cells are generation 0; each division increments by 1.</summary>
    public required int Generation { get; init; }

    /// <summary>When this cell was born (registered for the first time).</summary>
    public required DateTimeOffset BornAt { get; init; }

    /// <summary>When this cell died. <see langword="null"/> while still alive.</summary>
    public DateTimeOffset? DiedAt { get; init; }

    /// <summary>Human-readable reason for the cell's death (e.g. <c>"idle"</c>, <c>"aged"</c>, <c>"rollback"</c>).</summary>
    public string? DeathReason { get; init; }

    /// <summary>The reason the parent divided to produce this cell.</summary>
    public string? DivisionReason { get; init; }

    /// <summary>Domains this cell inherited from its parent's division plan.</summary>
    public IReadOnlyList<string> InheritedDomains { get; init; } = [];

    /// <summary>
    /// Summary of the cell's structural genome at birth (e.g. tool names, job names).
    /// Useful for lineage visualisation without requiring the full manifest.
    /// </summary>
    public IReadOnlyDictionary<string, string> GenomeSummary { get; init; } =
        new Dictionary<string, string>();
}
