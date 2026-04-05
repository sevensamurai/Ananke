namespace Ananke.Abstractions.Channels;

/// <summary>
/// Generic background consumer/worker.
/// </summary>
/// <typeparam name="T">The item type to process.</typeparam>
public interface IBackgroundWorker<in T>
{
    /// <summary>
    /// Handles a single item from the channel.
    /// </summary>
    Task HandleAsync(T? item, CancellationToken token);
}

/// <summary>
/// Background consumer/worker that receives items together with a typed action.
/// Used by <see cref="IChannelReader{M, A}"/> to deliver the parsed action enum
/// alongside the message — eliminating the need for marker interfaces like
/// <c>IMqttContext</c> on the domain model.
/// </summary>
/// <typeparam name="T">The item type to process.</typeparam>
/// <typeparam name="A">Action/transition enum type.</typeparam>
public interface IBackgroundWorker<in T, in A>
    where A : Enum
{
    /// <summary>
    /// Handles a single item together with the action parsed from the channel topic.
    /// </summary>
    Task HandleAsync(T? item, A action, CancellationToken token);
}
