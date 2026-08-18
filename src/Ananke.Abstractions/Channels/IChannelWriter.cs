using Ananke.Abstractions.Config;

namespace Ananke.Abstractions.Channels;

/// <summary>
/// Result of a channel send operation
/// </summary>
public record ChannelSendResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static ChannelSendResult Succeeded() => new() { Success = true };
    public static ChannelSendResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Basic channel writer interface
/// </summary>
public interface IChannelWriter : IAsyncDisposable
{
    /// <summary>
    /// Configures the channel connection
    /// </summary>
    Task<bool> ConfigureAsync(ChannelConfig credentials, CancellationToken ct = default);

    /// <summary>
    /// Clears/disconnects the channel
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a message to the channel
    /// </summary>
    Task<ChannelSendResult> SendAsync(object message, CancellationToken ct = default);

    /// <summary>
    /// Whether the channel is connected
    /// </summary>
    bool IsConnected { get; }
}

/// <summary>
/// Typed channel writer with action support
/// </summary>
/// <typeparam name="A">Action/transition enum type</typeparam>
public interface IChannelWriter<A> : IAsyncDisposable where A : Enum
{
    /// <summary>
    /// Configures the channel connection
    /// </summary>
    Task<bool> ConfigureAsync(ChannelConfig credentials, CancellationToken token = default);

    /// <summary>
    /// Clears/disconnects the channel
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a message with an associated action
    /// </summary>
    Task<ChannelSendResult> SendAsync(object message, A action, CancellationToken ct = default);

    /// <summary>
    /// Whether the channel is connected
    /// </summary>
    bool IsConnected { get; }
}
