using SlackNet;
using SlackNet.Events;

namespace Ananke.Platforms.Slack;

/// <summary>
/// SlackNet <see cref="IEventHandler{T}"/> that bridges incoming
/// <see cref="MessageEvent"/>s to the
/// <see cref="SlackAdapter.DispatchAsync(MessageEvent, CancellationToken)"/> pipeline.
/// Registered during <see cref="ServiceCollectionExtensions.AddAnankeSlack"/> configuration.
/// </summary>
internal sealed class SlackMessageEventHandler(SlackAdapter adapter) : IEventHandler<MessageEvent>
{
    public Task Handle(MessageEvent slackEvent)
    {
        if (string.IsNullOrEmpty(slackEvent.User))
            return Task.CompletedTask;

        // 5.4: Route through BoundedDispatcher so message dispatch is bounded in memory
        // and observed errors are logged rather than silently dropped.
        adapter.EnqueueDispatch(slackEvent);
        return Task.CompletedTask;
    }
}
