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
    public decimal TotalCost { get; init; }
    public decimal CostPerSuccessfulTrajectory { get; init; }
    public TimeSpan Duration { get; init; }

    public float ToolEfficiency =>
        TotalToolCalls > 0 ? (float)SuccessfulToolCalls / TotalToolCalls : 0f;

    public float RecoveryRate =>
        (RecoveredFaults + AbandonedFaults) > 0
            ? (float)RecoveredFaults / (RecoveredFaults + AbandonedFaults) : 0f;
}
