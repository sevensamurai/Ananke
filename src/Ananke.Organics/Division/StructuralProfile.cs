namespace Ananke.Organics.Division;

/// <summary>
/// Static structural metrics for a cell, derived from the manifest and tool
/// definitions. Combined with execution telemetry by
/// <see cref="WorkflowExecutionMonitor"/> to produce a <see cref="ComplexitySnapshot"/>.
/// </summary>
public sealed record StructuralProfile
{
    /// <summary>Total tools bound across all agent jobs.</summary>
    public required int ToolCount { get; init; }

    /// <summary>Number of jobs in the workflow topology.</summary>
    public required int JobCount { get; init; }

    /// <summary>
    /// Number of distinct tag clusters detected by co-occurrence analysis.
    /// High values (≥2) suggest natural domain boundaries for division.
    /// </summary>
    public required int TagClusterCount { get; init; }

    /// <summary>
    /// Count of distinct external backends/APIs/data sources the cell's
    /// tools reach.
    /// </summary>
    public required int ResourceSpan { get; init; }

    /// <summary>
    /// Fraction of the LLM's effective context window consumed by tool
    /// definitions alone (0.0–1.0).
    /// </summary>
    public required float ContextUtilization { get; init; }
}
