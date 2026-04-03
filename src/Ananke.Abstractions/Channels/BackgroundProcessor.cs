using System.Threading.Channels;

namespace Ananke.Abstractions.Channels;

/// <summary>
/// Bounded queue that dispatches items to an <see cref="IBackgroundWorker{T}"/> on a background loop.
/// Provides backpressure, error isolation, and clean shutdown semantics.
/// </summary>
/// <remarks>
/// <para>
/// Use this to decouple a fast producer (e.g., an MQTT event handler) from a potentially
/// slow or error-prone consumer. The producer enqueues via <see cref="EnqueueAsync"/> or
/// <see cref="TryEnqueue"/>; the internal loop dispatches to the worker sequentially.
/// </para>
/// <para>
/// Worker exceptions are reported via the optional <c>onError</c> callback
/// and do not stop the processing loop. Cancellation and disposal are handled gracefully.
/// </para>
/// </remarks>
/// <typeparam name="T">The item type to process.</typeparam>
public sealed class BackgroundProcessor<T> : IAsyncDisposable where T : class
{
    private readonly IBackgroundWorker<T> _worker;
    private readonly Channel<T> _queue;
    private readonly Action<Exception, T?>? _onError;
    private readonly Action<string>? _onInfo;
    private CancellationTokenSource? _cts;
    private Task _processingTask = Task.CompletedTask;
    private bool _disposed;

    /// <summary>
    /// Creates a new background processor.
    /// </summary>
    /// <param name="worker">The worker that handles each dequeued item.</param>
    /// <param name="capacity">
    /// Maximum number of items buffered before <see cref="EnqueueAsync"/> applies backpressure.
    /// Defaults to 1024.
    /// </param>
    /// <param name="onError">
    /// Optional callback invoked when the worker throws a non-cancellation exception.
    /// Receives the exception and the item that caused it (if available).
    /// </param>
    /// <param name="onInfo">
    /// Optional callback for informational messages (e.g., shutdown notifications).
    /// </param>
    public BackgroundProcessor(
        IBackgroundWorker<T> worker,
        int capacity = 1024,
        Action<Exception, T?>? onError = null,
        Action<string>? onInfo = null)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _worker = worker;
        _onError = onError;
        _onInfo = onInfo;
        _queue = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>
    /// Starts the background processing loop. Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    /// <param name="ct">
    /// Cancellation token that stops the processing loop. Linked internally so that
    /// <see cref="DisposeAsync"/> also cancels.
    /// </param>
    public void Start(CancellationToken ct = default)
    {
        if (_cts is not null) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _processingTask = Task.Run(() => ProcessAsync(_cts.Token));
    }

    /// <summary>
    /// Whether the processing loop has been started and has not yet completed.
    /// </summary>
    public bool IsRunning => _cts is not null && !_processingTask.IsCompleted;

    /// <summary>
    /// Enqueues an item for processing. Applies backpressure when the internal buffer is full.
    /// </summary>
    public ValueTask EnqueueAsync(T item, CancellationToken ct = default)
        => _queue.Writer.WriteAsync(item, ct);

    /// <summary>
    /// Tries to enqueue an item without waiting. Returns <see langword="false"/> if the buffer is full.
    /// </summary>
    public bool TryEnqueue(T item)
        => _queue.Writer.TryWrite(item);

    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await _worker.HandleAsync(item, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _onError?.Invoke(ex, item);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _onInfo?.Invoke("Processing stopped (cancellation requested)");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _queue.Writer.TryComplete();

        if (_cts is not null)
        {
            await _cts.CancelAsync();
            try { await _processingTask; }
            catch { /* ProcessAsync handles its own errors */ }
            _cts.Dispose();
        }
    }
}
