using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Platforms;

/// <summary>
/// Queues fire-and-forget platform message dispatches through a bounded
/// <see cref="Channel{T}"/> so that slow handlers cannot cause unbounded
/// memory growth when events arrive faster than they are processed.
/// </summary>
/// <remarks>
/// <para>
/// Create one instance per adapter and call <see cref="StartAsync"/> when the
/// adapter connects. Call <see cref="StopAsync"/> to drain the queue and
/// stop the background worker before disconnecting.
/// </para>
/// <para>
/// When the channel is full (i.e., <c>capacity</c> items are pending) the oldest
/// item is dropped and a warning is logged so throughput is preserved over
/// memory safety.
/// </para>
/// </remarks>
public sealed class BoundedDispatcher : IAsyncDisposable
{
    private readonly Channel<Func<CancellationToken, Task>> _channel;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    /// <param name="capacity">
    /// Maximum number of pending dispatch items. When full, the oldest item is
    /// dropped and a warning is emitted. Default is 256.
    /// </param>
    /// <param name="logger">Optional logger for drop warnings and worker faults.</param>
    public BoundedDispatcher(int capacity = 256, ILogger? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _channel = Channel.CreateBounded<Func<CancellationToken, Task>>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Starts the background dispatch worker.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _worker = Task.Run(() => RunWorkerAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Signals the worker to stop, drains remaining items, then waits for
    /// the worker task to complete.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        _channel.Writer.TryComplete();

        if (_cts is not null)
            await _cts.CancelAsync();

        if (_worker is not null)
        {
            try { await _worker.WaitAsync(ct); }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>
    /// Enqueues <paramref name="work"/> for dispatch on the background worker.
    /// If the channel is full the oldest pending item is silently dropped (logged at Warning).
    /// </summary>
    public void Enqueue(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!_channel.Writer.TryWrite(work))
            _logger.LogWarning(
                "[BoundedDispatcher] Channel full — a dispatch item was dropped. " +
                "Consider increasing the dispatcher capacity.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }

    private async Task RunWorkerAsync(CancellationToken ct)
    {
        await foreach (var work in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await work(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BoundedDispatcher] Unhandled exception in dispatched work item.");
            }
        }
    }
}
