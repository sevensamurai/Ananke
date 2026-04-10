using SlackNet;
using SlackNet.Events;

namespace Ananke.Platforms.Slack;

/// <summary>
/// SlackNet <see cref="IEventHandler{T}"/> that bridges incoming
/// <see cref="MessageEvent"/>s to the <see cref="SlackAdapter.DispatchAsync"/> pipeline.
/// Registered during <see cref="ServiceCollectionExtensions.AddAnankeSlack"/> configuration.
/// </summary>
internal sealed class SlackMessageEventHandler(SlackAdapter adapter) : IEventHandler<MessageEvent>
{
    public Task Handle(MessageEvent slackEvent)
    {
        if (string.IsNullOrEmpty(slackEvent.User))
            return Task.CompletedTask;

        return adapter.DispatchAsync(slackEvent);
    }
}
