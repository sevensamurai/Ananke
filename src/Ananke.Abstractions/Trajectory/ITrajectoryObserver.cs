namespace Ananke.Abstractions.Trajectory;

/// <summary>
/// Observer contract — implement to receive <see cref="TrajectorySnapshot"/> notifications
/// from the orchestration layer as each episode completes.
/// </summary>
public interface ITrajectoryObserver
{
    ValueTask OnTrajectoryCompleteAsync(
        TrajectorySnapshot snapshot,
        CancellationToken ct = default);
}

/// <summary>Null-object default implementation — discards all snapshots.</summary>
public sealed class NullTrajectoryObserver : ITrajectoryObserver
{
    public static readonly NullTrajectoryObserver Instance = new();

    public ValueTask OnTrajectoryCompleteAsync(
        TrajectorySnapshot snapshot, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
