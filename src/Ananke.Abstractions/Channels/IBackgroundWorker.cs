namespace Ananke.Abstractions.Channels;

/// <summary>
/// Generic background consumer/worker
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IBackgroundWorker<in T>
{
    public Task HandleAsync(T? item, CancellationToken token);
}
