using SlackNet;
using SlackNet.Events;

namespace Ananke.Platforms.Slack;

/// <summary>
/// SlackNet <see cref="IEventHandler{T}"/> that routes <see cref="AppMention"/> events
/// through the adapter's bounded dispatch pipeline.
/// </summary>
internal sealed class SlackAppMentionEventHandler(SlackAdapter adapter) : IEventHandler<AppMention>
{
    public Task Handle(AppMention slackEvent)
    {
        if (string.IsNullOrEmpty(slackEvent.User))
            return Task.CompletedTask;

        adapter.EnqueueDispatch(slackEvent);
        return Task.CompletedTask;
    }
}
