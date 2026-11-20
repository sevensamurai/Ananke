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
/// and do not stop the processing loop. An exception thrown by <c>onError</c> itself is
/// reported via <c>onInfo</c>, and the loop goes on.
/// </para>
/// <para>
/// Two ways to stop: <see cref="CompleteAsync"/> refuses new items and returns once every
/// queued item was handled; <see cref="DisposeAsync"/> cancels the loop and drops what is
/// still queued.
/// </para>
/// </remarks>
/// <typeparam name="T">The item type to process.</typeparam>
public sealed class BackgroundProcessor<T> : IAsyncDisposable where T : class
{
    private readonly BackgroundLoop<T> _loop;

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
    /// Optional callback for informational messages (e.g., shutdown notifications, or an
    /// exception thrown by <paramref name="onError"/>).
    /// </param>
    public BackgroundProcessor(
        IBackgroundWorker<T> worker,
        int capacity = 1024,
        Action<Exception, T?>? onError = null,
        Action<string>? onInfo = null)
    {
        ArgumentNullException.ThrowIfNull(worker);

        _loop = new BackgroundLoop<T>(
            typeof(BackgroundProcessor<T>),
            worker.HandleAsync,
            capacity,
            onError,
            onInfo);
    }

    /// <summary>
    /// Starts the background processing loop. Safe to call more than once, from any thread —
    /// only the first call starts a loop.
    /// </summary>
    /// <param name="ct">
    /// Cancellation token that stops the processing loop. Linked internally so that
    /// <see cref="DisposeAsync"/> also cancels.
    /// </param>
    /// <exception cref="ObjectDisposedException">The processor was disposed.</exception>
    public void Start(CancellationToken ct = default) => _loop.Start(ct);

    /// <summary>
    /// Whether the processing loop has been started and has not yet completed.
    /// </summary>
    public bool IsRunning => _loop.IsRunning;

    /// <summary>
    /// Enqueues an item for processing. Applies backpressure when the internal buffer is full.
    /// </summary>
    /// <exception cref="ChannelClosedException">
    /// <see cref="CompleteAsync"/> or <see cref="DisposeAsync"/> was called.
    /// </exception>
    public ValueTask EnqueueAsync(T item, CancellationToken ct = default) => _loop.EnqueueAsync(item, ct);

    /// <summary>
    /// Tries to enqueue an item without waiting. Returns <see langword="false"/> if the buffer is full,
    /// or once <see cref="CompleteAsync"/> or <see cref="DisposeAsync"/> was called.
    /// </summary>
    public bool TryEnqueue(T item) => _loop.TryEnqueue(item);

    /// <summary>
    /// Refuses further items and returns once every item already queued was handled.
    /// Starts the loop if it was not started, so items queued before are handled too.
    /// </summary>
    /// <param name="ct">
    /// Stops waiting; the loop goes on with the queue. To drop what is left, call <see cref="DisposeAsync"/>.
    /// </param>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was cancelled, or the loop was stopped (the token given to <see cref="Start"/>,
    /// or <see cref="DisposeAsync"/>) before the queue was empty.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The processor was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// Called from the worker, <c>onError</c> or <c>onInfo</c>, or from a task one of them started, while
    /// the loop runs: it would wait for the loop it runs on. Thrown at the call, not by the returned task.
    /// </exception>
    public Task CompleteAsync(CancellationToken ct = default) => _loop.CompleteAsync(ct);

    /// <summary>
    /// Refuses further items, cancels the loop and drops the items still queued.
    /// Use <see cref="CompleteAsync"/> first to handle them. Every call returns once the loop has
    /// stopped.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from the worker, <c>onError</c> or <c>onInfo</c>, or from a task one of them started, while
    /// the loop runs: it would wait for the loop it runs on. Thrown at the call, not by the returned task.
    /// </exception>
    public ValueTask DisposeAsync() => _loop.DisposeAsync();
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
    private readonly BackgroundLoop<(T Item, A Action)> _loop;

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
    /// Optional callback for informational messages (e.g., shutdown notifications, or an
    /// exception thrown by <paramref name="onError"/>).
    /// </param>
    public BackgroundProcessor(
        IBackgroundWorker<T, A> worker,
        int capacity = 1024,
        Action<Exception, T?>? onError = null,
        Action<string>? onInfo = null)
    {
        ArgumentNullException.ThrowIfNull(worker);

        _loop = new BackgroundLoop<(T Item, A Action)>(
            typeof(BackgroundProcessor<T, A>),
            (queued, ct) => worker.HandleAsync(queued.Item, queued.Action, ct),
            capacity,
            onError is null ? null : (ex, queued) => onError(ex, queued.Item),
            onInfo);
    }

    /// <inheritdoc cref="BackgroundProcessor{T}.Start"/>
    public void Start(CancellationToken ct = default) => _loop.Start(ct);

    /// <inheritdoc cref="BackgroundProcessor{T}.IsRunning"/>
    public bool IsRunning => _loop.IsRunning;

    /// <summary>
    /// Enqueues an item with its associated action for processing.
    /// </summary>
    /// <exception cref="ChannelClosedException">
    /// <see cref="CompleteAsync"/> or <see cref="DisposeAsync"/> was called.
    /// </exception>
    public ValueTask EnqueueAsync(T item, A action, CancellationToken ct = default)
        => _loop.EnqueueAsync((item, action), ct);

    /// <summary>
    /// Tries to enqueue an item without waiting. Returns <see langword="false"/> if the buffer is full,
    /// or once <see cref="CompleteAsync"/> or <see cref="DisposeAsync"/> was called.
    /// </summary>
    public bool TryEnqueue(T item, A action) => _loop.TryEnqueue((item, action));

    /// <inheritdoc cref="BackgroundProcessor{T}.CompleteAsync"/>
    public Task CompleteAsync(CancellationToken ct = default) => _loop.CompleteAsync(ct);

    /// <inheritdoc cref="BackgroundProcessor{T}.DisposeAsync"/>
    public ValueTask DisposeAsync() => _loop.DisposeAsync();
}
