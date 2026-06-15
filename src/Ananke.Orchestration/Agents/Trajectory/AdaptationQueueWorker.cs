using System.Threading.Channels;
using Ananke.Abstractions.Trajectory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Agents.Trajectory;

/// <summary>
/// Hosted service that drains the adaptation <see cref="Channel{T}"/> and calls
/// <see cref="IAdaptiveHarnessPolicy.AdaptAsync"/> off the trajectory-completion hot path.
/// </summary>
internal sealed class AdaptationQueueWorker(
    Channel<TrajectorySnapshot> channel,
    IAdaptiveHarnessPolicy policy,
    ILogger<AdaptationQueueWorker>? logger = null) : BackgroundService
{
    private readonly ILogger<AdaptationQueueWorker> _logger =
        logger ?? NullLogger<AdaptationQueueWorker>.Instance;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var snapshot in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await policy.AdaptAsync(snapshot, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[AdaptationQueueWorker] Adaptation failed for episode {EpisodeId}; skipping.",
                    snapshot.EpisodeId);
            }
        }
    }
}
