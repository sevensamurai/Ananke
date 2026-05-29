using SlackNet;
using SlackNet.Events;

namespace Ananke.Platforms.Slack;

/// <summary>
/// SlackNet <see cref="IEventHandler{T}"/> that routes <see cref="ReactionAdded"/> events
/// through the adapter's bounded dispatch pipeline.
/// </summary>
internal sealed class SlackReactionEventHandler(SlackAdapter adapter) : IEventHandler<ReactionAdded>
{
    public Task Handle(ReactionAdded slackEvent)
    {
        if (string.IsNullOrEmpty(slackEvent.User))
            return Task.CompletedTask;

        adapter.EnqueueReaction(slackEvent);
        return Task.CompletedTask;
    }
}
