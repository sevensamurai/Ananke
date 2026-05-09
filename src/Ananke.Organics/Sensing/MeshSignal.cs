namespace Ananke.Organics.Sensing;

/// <summary>
/// Mesh-wide metabolic signal aggregated across all living cells.
/// Emitted by <see cref="IMeshAggregator"/> whenever the stress ratio
/// changes by more than a configurable delta.
/// </summary>
public sealed record MeshSignal
{
    /// <summary>Total number of cells reporting into the aggregator.</summary>
    public required int TotalCells { get; init; }

    /// <summary>Number of cells currently in <see cref="Division.MetabolicSignal.Stressed"/> state.</summary>
    public required int StressedCells { get; init; }

    /// <summary>Number of cells currently in <see cref="Division.MetabolicSignal.Starved"/> state.</summary>
    public required int StarvedCells { get; init; }

    /// <summary>Fraction of cells that are stressed (0.0–1.0). 0 when <see cref="TotalCells"/> is zero.</summary>
    public double StressRatio => TotalCells == 0 ? 0 : (double)StressedCells / TotalCells;

    /// <summary>When this signal was sampled.</summary>
    public required DateTimeOffset SampledAt { get; init; }
}
