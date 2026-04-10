using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Ananke.Platforms.Discord;

/// <summary>
/// <see cref="IPlatformResponseSink"/> implementation backed by Discord's WebSocket API.
/// Maps Ananke response operations to Discord <c>SendMessageAsync</c>,
/// <c>ModifyAsync</c>, <c>AddReactionAsync</c>, etc.
/// </summary>
/// <remarks>
/// <para>
/// Message IDs returned by <see cref="SendMessageAsync"/> are composite strings
/// (<c>channelId:messageId</c>) so that <see cref="UpdateMessageAsync"/> and
/// <see cref="AddReactionAsync"/> can resolve the correct channel without
/// additional state. This is an internal implementation detail — callers
/// (including <see cref="StreamingMessageBridge"/>) treat the ID as opaque.
/// </para>
/// </remarks>
internal sealed class DiscordResponseSink(DiscordSocketClient client, ILogger? logger = null) : IPlatformResponseSink
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <inheritdoc />
    public async Task<string> SendMessageAsync(string channelId, string? threadId, string text,
        CancellationToken ct = default)
    {
        var effectiveChannelId = ulong.Parse(threadId ?? channelId);
        var channel = client.GetChannel(effectiveChannelId) as IMessageChannel
            ?? throw new InvalidOperationException(
                $"Discord channel {effectiveChannelId} not found or is not a message channel.");

        var message = await channel.SendMessageAsync(text, options: new RequestOptions { CancelToken = ct })
            .ConfigureAwait(false);

        _logger.LogDebug("Discord: posted message {MessageId} to channel {ChannelId}",
            message.Id, effectiveChannelId);

        // Encode effective channel into the ID so Update/Reaction can resolve it
        return $"{effectiveChannelId}:{message.Id}";
    }

    /// <inheritdoc />
    public async Task UpdateMessageAsync(string channelId, string messageId, string text,
        CancellationToken ct = default)
    {
        var (effectiveChannelId, discordMessageId) = ParseCompositeMessageId(messageId);
        var channel = client.GetChannel(effectiveChannelId) as IMessageChannel
            ?? throw new InvalidOperationException(
                $"Discord channel {effectiveChannelId} not found or is not a message channel.");

        var options = new RequestOptions { CancelToken = ct };
        if (await channel.GetMessageAsync(discordMessageId, options: options).ConfigureAwait(false)
            is IUserMessage msg)
        {
            await msg.ModifyAsync(m => m.Content = text, options).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SendTypingAsync(string channelId, string? threadId,
        CancellationToken ct = default)
    {
        var effectiveChannelId = ulong.Parse(threadId ?? channelId);
        if (client.GetChannel(effectiveChannelId) is IMessageChannel channel)
        {
            await channel.TriggerTypingAsync(new RequestOptions { CancelToken = ct })
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task AddReactionAsync(string channelId, string messageId, string emoji,
        CancellationToken ct = default)
    {
        var (effectiveChannelId, discordMessageId) = ParseCompositeMessageId(messageId);
        var channel = client.GetChannel(effectiveChannelId) as IMessageChannel
            ?? throw new InvalidOperationException(
                $"Discord channel {effectiveChannelId} not found or is not a message channel.");

        var options = new RequestOptions { CancelToken = ct };
        if (await channel.GetMessageAsync(discordMessageId, options: options).ConfigureAwait(false)
            is IUserMessage msg)
        {
            await msg.AddReactionAsync(new Emoji(emoji), options).ConfigureAwait(false);
        }
    }

    private static (ulong ChannelId, ulong MessageId) ParseCompositeMessageId(string compositeId)
    {
        var separatorIndex = compositeId.IndexOf(':');
        if (separatorIndex < 0)
            throw new ArgumentException(
                $"Invalid Discord message ID format: '{compositeId}'. Expected 'channelId:messageId'.",
                nameof(compositeId));

        return (
            ulong.Parse(compositeId.AsSpan(0, separatorIndex)),
            ulong.Parse(compositeId.AsSpan(separatorIndex + 1))
        );
    }
}
