using System.Threading.Channels;
using Ananke.Abstractions.Trajectory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Agents.Trajectory;

/// <summary>
/// Non-blocking <see cref="ITrajectoryObserver"/> that writes snapshots to a
/// bounded <see cref="Channel{T}"/> for processing by <see cref="AdaptationQueueWorker"/>.
/// </summary>
internal sealed class ChannelTrajectoryObserver(
    Channel<TrajectorySnapshot> channel,
    ILogger<ChannelTrajectoryObserver>? logger = null) : ITrajectoryObserver
{
    private readonly ILogger<ChannelTrajectoryObserver> _logger =
        logger ?? NullLogger<ChannelTrajectoryObserver>.Instance;

    public ValueTask OnTrajectoryCompleteAsync(TrajectorySnapshot snapshot, CancellationToken ct = default)
    {
        if (!channel.Writer.TryWrite(snapshot))
            _logger.LogWarning(
                "[AdaptiveHarness] Adaptation channel is full; snapshot for episode {EpisodeId} dropped.",
                snapshot.EpisodeId);
        return ValueTask.CompletedTask;
    }
}
