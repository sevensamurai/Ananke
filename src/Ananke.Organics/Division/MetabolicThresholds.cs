namespace Ananke.Organics.Division;

/// <summary>
/// Configurable thresholds that determine whether a cell's metabolic signal
/// is classified as <see cref="MetabolicSignal.Stressed"/> or
/// <see cref="MetabolicSignal.Starved"/>.
/// </summary>
public sealed record MetabolicThresholds
{
    /// <summary>
    /// Default thresholds suitable for most in-process workloads.
    /// </summary>
    public static readonly MetabolicThresholds Default = new();

    /// <summary>
    /// Error rate at or above which a cell is considered <see cref="MetabolicSignal.Stressed"/>.
    /// Default: 0.10 (10 %).
    /// </summary>
    public double StressedErrorRate { get; init; } = 0.10;

    /// <summary>
    /// Error rate at or above which a cell is considered <see cref="MetabolicSignal.Starved"/>.
    /// Default: 0.30 (30 %).
    /// </summary>
    public double StarvedErrorRate { get; init; } = 0.30;

    /// <summary>
    /// P95 latency (ms) at or above which a cell is considered <see cref="MetabolicSignal.Stressed"/>.
    /// Default: 5 000 ms.
    /// </summary>
    public double StressedLatencyP95Ms { get; init; } = 5_000;

    /// <summary>
    /// P95 latency (ms) at or above which a cell is considered <see cref="MetabolicSignal.Starved"/>.
    /// Default: 15 000 ms.
    /// </summary>
    public double StarvedLatencyP95Ms { get; init; } = 15_000;

    /// <summary>
    /// Derive the <see cref="MetabolicSignal"/> for a snapshot using these thresholds.
    /// Returns <see cref="MetabolicSignal.Healthy"/> when no metabolic data is available.
    /// </summary>
    public MetabolicSignal Classify(ComplexitySnapshot snapshot)
    {
        var errorRate = snapshot.ErrorRate;
        var latency = snapshot.LatencyP95Ms;

        // Starved wins over Stressed
        if ((errorRate >= StarvedErrorRate) || (latency >= StarvedLatencyP95Ms))
            return MetabolicSignal.Starved;

        if ((errorRate >= StressedErrorRate) || (latency >= StressedLatencyP95Ms))
            return MetabolicSignal.Stressed;

        return MetabolicSignal.Healthy;
    }
}
