using Ananke.Abstractions.Config;

namespace Ananke.Abstractions.Channels;

/// <summary>
/// In-memory <see cref="IChannelReader{M, A}"/> for testing and single-process demos.
/// Pair with <see cref="InMemoryChannelWriter{A}"/> — the writer enqueues directly into
/// the reader's processing pipeline without any network transport.
/// </summary>
/// <typeparam name="M">Message type.</typeparam>
/// <typeparam name="A">Action/transition enum type.</typeparam>
public sealed class InMemoryChannelReader<M, A> : IChannelReader<M, A>
    where M : class
    where A : Enum
{
    private BackgroundProcessor<M, A>? _processor;
    private bool _disposed;

    /// <summary>
    /// Directly enqueue a message with its action into the processing pipeline.
    /// Called by <see cref="InMemoryChannelWriter{A}"/> when linked.
    /// </summary>
    internal ValueTask DeliverAsync(M message, A action)
    {
        if (_processor is null)
            throw new InvalidOperationException("Reader not configured. Call ConfigureAsync first.");
        return _processor.EnqueueAsync(message, action);
    }

    /// <inheritdoc />
    public Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, A action, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        // Wrap the untyped worker to match the typed-action signature
        var bridge = new BridgeWorker<M, A>(consumer);
        return ConfigureAsync(config, bridge, token);
    }

    /// <inheritdoc />
    public Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var bridge = new BridgeWorker<M, A>(consumer);
        return ConfigureAsync(config, bridge, token);
    }

    /// <inheritdoc />
    public Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M, A> consumer, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(consumer);

        _processor?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        _processor = new BackgroundProcessor<M, A>(consumer);
        _processor.Start(token);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        if (_processor is not null)
            return _processor.DisposeAsync().AsTask();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_processor is not null)
            await _processor.DisposeAsync();
    }

    /// <summary>
    /// Adapts an <see cref="IBackgroundWorker{T}"/> to <see cref="IBackgroundWorker{T, A}"/>
    /// by discarding the action parameter.
    /// </summary>
    private sealed class BridgeWorker<T, TAction>(IBackgroundWorker<T> inner)
        : IBackgroundWorker<T, TAction>
        where TAction : Enum
    {
        public Task HandleAsync(T? item, TAction action, CancellationToken token)
            => inner.HandleAsync(item, token);
    }
}
