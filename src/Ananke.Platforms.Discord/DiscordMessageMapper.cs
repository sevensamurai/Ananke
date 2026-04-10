using Ananke.Abstractions.Agents;
using Discord.WebSocket;

namespace Ananke.Platforms.Discord;

/// <summary>
/// Maps Discord <see cref="SocketMessage"/> events to <see cref="PlatformMessage"/> instances.
/// </summary>
internal static class DiscordMessageMapper
{
    /// <summary>
    /// Converts a Discord <see cref="SocketMessage"/> to a normalized <see cref="PlatformMessage"/>.
    /// </summary>
    /// <remarks>
    /// In Discord, threads are channels (<see cref="SocketThreadChannel"/>).
    /// When a message arrives in a thread, the mapper sets <see cref="PlatformMessage.ChannelId"/>
    /// to the parent channel and <see cref="PlatformMessage.ThreadId"/> to the thread channel.
    /// For regular channel messages, <see cref="PlatformMessage.ThreadId"/> is <see langword="null"/>.
    /// </remarks>
    internal static PlatformMessage FromDiscordMessage(SocketMessage socketMessage)
    {
        string channelId;
        string? threadId = null;

        if (socketMessage.Channel is SocketThreadChannel thread)
        {
            channelId = thread.ParentChannel.Id.ToString();
            threadId = thread.Id.ToString();
        }
        else
        {
            channelId = socketMessage.Channel.Id.ToString();
        }

        return new PlatformMessage
        {
            ChannelId = channelId,
            ThreadId = threadId,
            UserId = socketMessage.Author.Id.ToString(),
            UserName = socketMessage.Author.Username,
            PlatformMessageId = socketMessage.Id.ToString(),
            Message = AgentMessage.User(socketMessage.Content ?? string.Empty),
            PlatformContext = socketMessage
        };
    }
}
