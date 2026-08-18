using Ananke.Abstractions.Config;

namespace Ananke.Abstractions.Channels;

/// <summary>
/// Channel reader that subscribes to messages and dispatches them to a background worker.
/// </summary>
/// <typeparam name="M">Message type.</typeparam>
public interface IChannelReader<M> : IAsyncDisposable
    where M : class
{
    /// <summary>
    /// Configures the channel subscription and starts dispatching to the worker.
    /// </summary>
    Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, CancellationToken token = default);

    /// <summary>
    /// Unsubscribes and disconnects the channel.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// Channel reader with action/transition routing support.
/// Subscribes to topics derived from the action enum and dispatches messages
/// to the provided background worker.
/// </summary>
/// <typeparam name="M">Message type.</typeparam>
/// <typeparam name="A">Action/transition enum type used for topic routing.</typeparam>
public interface IChannelReader<M, A> : IAsyncDisposable
    where M : class
    where A : Enum
{
    /// <summary>
    /// Subscribes to a specific action topic and dispatches to an untyped worker.
    /// The action is not forwarded to the worker.
    /// </summary>
    Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, A action, CancellationToken token = default);

    /// <summary>
    /// Subscribes to all action topics (wildcard) and dispatches to an untyped worker.
    /// For <see cref="IBackgroundWorker{T}"/> consumers, the action is set on the message
    /// if it implements a command interface. Prefer the <see cref="IBackgroundWorker{T, A}"/>
    /// overload instead.
    /// </summary>
    Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, CancellationToken token = default);

    /// <summary>
    /// Subscribes to all action topics (wildcard) and dispatches to a typed-action worker.
    /// The action enum is parsed from the channel topic and delivered alongside the message —
    /// no <c>IMqttContext.Command</c> string needed on the domain model.
    /// </summary>
    Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M, A> consumer, CancellationToken token = default);

    /// <summary>
    /// Unsubscribes and disconnects the channel.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);
}
