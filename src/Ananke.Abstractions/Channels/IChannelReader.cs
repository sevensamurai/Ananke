using Ananke.Abstractions.Config;

namespace Ananke.Abstractions.Channels;

public interface IChannelReader<M> 
    where M : class
{
    Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, CancellationToken token = default);
    Task Clear();
}

public interface IChannelReader<M, A> 
    where M : class
    where A : Enum
{
    Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, A action, CancellationToken token = default);
    Task<bool> ConfigureAsync(ChannelConfig config, IBackgroundWorker<M> consumer, CancellationToken token = default);
    Task Clear();
}
