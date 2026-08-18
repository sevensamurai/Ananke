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
        _processingTask = Task.Run(() => ProcessAsync(_cts.Token), _cts.Token);
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
            await foreach (var item in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await _worker.HandleAsync(item, ct).ConfigureAwait(false);
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
            await _cts.CancelAsync().ConfigureAwait(false);
            try { await _processingTask.ConfigureAwait(false); }
            catch { /* ProcessAsync handles its own errors */ }
            _cts.Dispose();
        }
    }
}

/// <summary>
/// Bounded queue that dispatches <c>(item, action)</c> pairs to an
/// <see cref="IBackgroundWorker{T, A}"/> on a background loop.
/// </summary>
/// <remarks>
/// Identical semantics to <see cref="BackgroundProcessor{T}"/> but carries a typed
/// action alongside each item — used by channel readers that parse the action from
/// the transport layer (e.g., MQTT topic segment) so the worker receives a strongly-typed
/// enum instead of a raw string.
/// </remarks>
/// <typeparam name="T">The item type to process.</typeparam>
/// <typeparam name="A">Action/transition enum type.</typeparam>
public sealed class BackgroundProcessor<T, A> : IAsyncDisposable
    where T : class
    where A : Enum
{
    private readonly IBackgroundWorker<T, A> _worker;
    private readonly Channel<(T Item, A Action)> _queue;
    private readonly Action<Exception, T?>? _onError;
    private readonly Action<string>? _onInfo;
    private CancellationTokenSource? _cts;
    private Task _processingTask = Task.CompletedTask;
    private bool _disposed;

    /// <summary>
    /// Creates a new typed-action background processor.
    /// </summary>
    /// <param name="worker">The worker that handles each dequeued <c>(item, action)</c> pair.</param>
    /// <param name="capacity">
    /// Maximum number of items buffered before <see cref="EnqueueAsync"/> applies backpressure.
    /// Defaults to 1024.
    /// </param>
    /// <param name="onError">
    /// Optional callback invoked when the worker throws a non-cancellation exception.
    /// </param>
    /// <param name="onInfo">
    /// Optional callback for informational messages (e.g., shutdown notifications).
    /// </param>
    public BackgroundProcessor(
        IBackgroundWorker<T, A> worker,
        int capacity = 1024,
        Action<Exception, T?>? onError = null,
        Action<string>? onInfo = null)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _worker = worker;
        _onError = onError;
        _onInfo = onInfo;
        _queue = Channel.CreateBounded<(T, A)>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>
    /// Starts the background processing loop. Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public void Start(CancellationToken ct = default)
    {
        if (_cts is not null) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _processingTask = Task.Run(() => ProcessAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// Whether the processing loop has been started and has not yet completed.
    /// </summary>
    public bool IsRunning => _cts is not null && !_processingTask.IsCompleted;

    /// <summary>
    /// Enqueues an item with its associated action for processing.
    /// </summary>
    public ValueTask EnqueueAsync(T item, A action, CancellationToken ct = default)
        => _queue.Writer.WriteAsync((item, action), ct);

    /// <summary>
    /// Tries to enqueue an item without waiting. Returns <see langword="false"/> if the buffer is full.
    /// </summary>
    public bool TryEnqueue(T item, A action)
        => _queue.Writer.TryWrite((item, action));

    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (item, action) in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await _worker.HandleAsync(item, action, ct).ConfigureAwait(false);
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
            await _cts.CancelAsync().ConfigureAwait(false);
            try { await _processingTask.ConfigureAwait(false); }
            catch { /* ProcessAsync handles its own errors */ }
            _cts.Dispose();
        }
    }
}
