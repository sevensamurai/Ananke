namespace Ananke.Abstractions.Trajectory;

/// <summary>
/// Immutable snapshot of a single trajectory's six harness signals, captured at episode completion.
/// </summary>
public sealed record TrajectorySnapshot
{
    public required string EpisodeId { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public float TerminalReward { get; init; }
    public bool Succeeded { get; init; }
    public int RetryCount { get; init; }
    public int TotalToolCalls { get; init; }
    public int SuccessfulToolCalls { get; init; }
    public int HallucinatedToolCalls { get; init; }
    public int FaultedToolCalls { get; init; }
    public int RecoveredFaults { get; init; }
    public int AbandonedFaults { get; init; }

    /// <summary>
    /// Tool calls whose name and arguments had already been made in this episode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cheap proxy for <em>forgot what it already knew</em>: asking the same question twice usually
    /// means the answer left the window between the two asks. Re-reading one file is the common
    /// case, and it is the same measurement restricted to read tools.
    /// </para>
    /// <para>
    /// Arguments are compared after being re-serialized, so formatting differences do not register
    /// as distinct — but two calls that differ only in property <em>order</em> still do.
    /// That under-counts rather than over-counts, which is the safe direction for a signal whose
    /// whole purpose is to say when something is going wrong.
    /// </para>
    /// </remarks>
    public int DuplicateToolCalls { get; init; }

    /// <summary>Distinct name-and-argument combinations called during the episode.</summary>
    public int DistinctToolCalls { get; init; }
    public decimal TotalCost { get; init; }
    public decimal CostPerSuccessfulTrajectory { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Share of tool calls that repeated an earlier one, in <c>[0,1]</c>. Zero when nothing ran.
    /// </summary>
    public float ReworkRate =>
        TotalToolCalls == 0 ? 0f : DuplicateToolCalls / (float)TotalToolCalls;

    public float ToolEfficiency =>
        TotalToolCalls > 0 ? (float)SuccessfulToolCalls / TotalToolCalls : 0f;

    public float RecoveryRate =>
        (RecoveredFaults + AbandonedFaults) > 0
            ? (float)RecoveredFaults / (RecoveredFaults + AbandonedFaults) : 0f;
}
