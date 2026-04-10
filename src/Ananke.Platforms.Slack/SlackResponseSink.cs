using Ananke.Platforms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlackNet;
using SlackNet.WebApi;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Ananke.Platforms.Slack;

/// <summary>
/// <see cref="IPlatformResponseSink"/> implementation backed by the Slack Web API.
/// Maps Ananke response operations to Slack <c>chat.postMessage</c>, <c>chat.update</c>,
/// <c>reactions.add</c>, etc.
/// </summary>
internal sealed class SlackResponseSink(ISlackApiClient api, ILogger? logger = null) : IPlatformResponseSink
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <inheritdoc />
    public async Task<string> SendMessageAsync(string channelId, string? threadId, string text,
        CancellationToken ct = default)
    {
        var response = await api.Chat.PostMessage(new Message
        {
            Channel = channelId,
            Text = text,
            ThreadTs = threadId
        }, ct).ConfigureAwait(false);

        _logger.LogDebug("Slack: posted message {Ts} to {Channel}", response.Ts, channelId);
        return response.Ts;
    }

    /// <inheritdoc />
    public async Task UpdateMessageAsync(string channelId, string messageId, string text,
        CancellationToken ct = default)
    {
        await api.Chat.Update(new MessageUpdate
        {
            ChannelId = channelId,
            Ts = messageId,
            Text = text
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SendTypingAsync(string channelId, string? threadId,
        CancellationToken ct = default)
    {
        // Slack does not have a public "typing indicator" API for bots.
        // Bots can show typing only in Socket Mode via the debug protocol,
        // which SlackNet does not expose. No-op.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task AddReactionAsync(string channelId, string messageId, string emoji,
        CancellationToken ct = default)
    {
        await api.Reactions.AddToMessage(emoji, channelId, messageId, ct)
            .ConfigureAwait(false);
    }
}
