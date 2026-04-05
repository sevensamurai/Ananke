using Ananke.Abstractions.Config;

namespace Ananke.Abstractions.Channels;

/// <summary>
/// In-memory <see cref="IChannelWriter{A}"/> for testing and single-process demos.
/// Link to an <see cref="InMemoryChannelReader{M, A}"/> via <see cref="LinkTo{M}"/>
/// to deliver messages directly into the reader's processing pipeline.
/// </summary>
/// <typeparam name="A">Action/transition enum type.</typeparam>
public sealed class InMemoryChannelWriter<A> : IChannelWriter<A>
    where A : Enum
{
    private Func<object, A, ValueTask>? _deliver;
    private bool _configured;

    /// <inheritdoc />
    public bool IsConnected => _configured && _deliver is not null;

    /// <summary>
    /// Links this writer to an in-memory reader so that <see cref="SendAsync"/>
    /// delivers messages directly into the reader's processing pipeline.
    /// </summary>
    /// <typeparam name="M">Message type matching the reader.</typeparam>
    /// <param name="reader">The reader to deliver messages to.</param>
    public InMemoryChannelWriter<A> LinkTo<M>(InMemoryChannelReader<M, A> reader)
        where M : class
    {
        ArgumentNullException.ThrowIfNull(reader);
        _deliver = (msg, action) => reader.DeliverAsync((M)msg, action);
        return this;
    }

    /// <inheritdoc />
    public Task<bool> ConfigureAsync(ChannelConfig credentials, CancellationToken token = default)
    {
        _configured = true;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendAsync(object message, A action)
    {
        if (!_configured)
            return ChannelSendResult.Failed("Writer not configured. Call ConfigureAsync first.");

        if (_deliver is null)
            return ChannelSendResult.Failed("Writer not linked to a reader. Call LinkTo first.");

        try
        {
            await _deliver(message, action);
            return ChannelSendResult.Succeeded();
        }
        catch (Exception ex)
        {
            return ChannelSendResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        _configured = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _configured = false;
        return ValueTask.CompletedTask;
    }
}
