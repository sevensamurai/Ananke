namespace Ananke.Organics.Division;

/// <summary>
/// Outcome verdict for a past division event. Used by <see cref="IDivisionPolicy"/>
/// to weight cluster strategies in subsequent decisions.
/// </summary>
public enum DivisionVerdict
{
    /// <summary>Child cells outperformed the parent on the key metabolic metrics.</summary>
    Improved,

    /// <summary>Child cell performance was roughly equivalent to the parent.</summary>
    Neutral,

    /// <summary>Child cells underperformed the parent — division was counterproductive.</summary>
    Regressed
}

/// <summary>
/// Delta metrics comparing child cell performance to the parent baseline
/// after a division event.
/// </summary>
public sealed record DivisionOutcomeMetrics
{
    /// <summary>
    /// Change in average tokens consumed per execution (child avg − parent baseline).
    /// Negative = improvement (fewer tokens).
    /// </summary>
    public double DeltaTokensPerExecution { get; init; }

    /// <summary>
    /// Change in error rate (child avg − parent baseline).
    /// Negative = improvement (fewer errors).
    /// </summary>
    public double DeltaErrorRate { get; init; }

    /// <summary>
    /// Change in 95th-percentile latency in ms (child avg − parent baseline).
    /// Negative = improvement (lower latency).
    /// </summary>
    public double DeltaLatencyP95 { get; init; }

    /// <summary>Days the child cells survived before the snapshot was taken.</summary>
    public int SurvivedDays { get; init; }
}

/// <summary>
/// A recorded outcome from a past division event in the same lineage.
/// Passed to <see cref="IDivisionPolicy.EvaluateAsync(ComplexitySnapshot, Ananke.Design.WorkflowManifest, IReadOnlyList{DivisionExperience}, CancellationToken)"/>
/// so the policy can weight strategies based on what worked before.
/// </summary>
public sealed record DivisionExperience
{
    /// <summary>Lineage identifier of the parent cell that divided.</summary>
    public required string LineageId { get; init; }

    /// <summary>The plan that was executed in this division event.</summary>
    public required DivisionPlan Plan { get; init; }

    /// <summary>Post-division outcome metrics.</summary>
    public required DivisionOutcomeMetrics Metrics { get; init; }

    /// <summary>Summary verdict computed from <see cref="Metrics"/>.</summary>
    public required DivisionVerdict Verdict { get; init; }
}
