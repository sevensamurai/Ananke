using Ananke.Abstractions.Agents;
using Ananke.Platforms;
using SlackNet.Events;

namespace Ananke.Platforms.Slack;

/// <summary>
/// Maps Slack events to <see cref="PlatformMessage"/> instances.
/// </summary>
internal static class SlackMessageMapper
{
    /// <summary>
    /// Converts a Slack <see cref="MessageEvent"/> to a normalized <see cref="PlatformMessage"/>.
    /// </summary>
    internal static PlatformMessage FromSlackEvent(MessageEvent slackEvent)
    {
        return new PlatformMessage
        {
            ChannelId = slackEvent.Channel ?? string.Empty,
            ThreadId = slackEvent.ThreadTs,
            UserId = slackEvent.User ?? "unknown",
            PlatformMessageId = slackEvent.Ts,
            Message = AgentMessage.User(slackEvent.Text ?? string.Empty),
            PlatformContext = slackEvent
        };
    }
}
