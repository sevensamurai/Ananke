using Ananke.Abstractions.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Memory;

/// <summary>
/// Hosted background service that periodically calls
/// <see cref="IConversationMemory.CleanupExpiredAsync"/> to remove sessions whose
/// TTL has elapsed. Registered automatically via
/// <see cref="Extensions.OrchestrationOptions.UseMemoryCleanup"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="BackgroundService"/> so the host starts and stops the
/// cleanup loop as part of normal application lifetime — no manual
/// <c>StartAsync()</c> call required.
/// </para>
/// <para>
/// Uses <see cref="PeriodicTimer"/> so ticks never overlap: the next tick only
/// fires after the previous <see cref="IConversationMemory.CleanupExpiredAsync"/>
/// call has fully completed.
/// </para>
/// </remarks>
internal sealed class ConversationMemoryCleanupTimer(
    IConversationMemory memory,
    TimeSpan interval,
    ILoggerFactory? loggerFactory = null,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly ILogger _logger = loggerFactory?.CreateLogger<ConversationMemoryCleanupTimer>()
        ?? NullLogger<ConversationMemoryCleanupTimer>.Instance;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TaskCompletionSource _timerReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes once <see cref="PeriodicTimer.WaitForNextTickAsync"/> has been entered
    /// for the first time and the service is ready to observe clock advances.
    /// Intended for use in tests that need deterministic synchronisation with a fake
    /// <see cref="TimeProvider"/>.
    /// </summary>
    internal Task TimerReady => _timerReady.Task;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        using var timer = new PeriodicTimer(interval, _timeProvider);
        _timerReady.TrySetResult();

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await memory.CleanupExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversation memory cleanup failed");
            }
        }
    }
}

