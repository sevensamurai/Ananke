using Ananke.Platforms;
using SlackNet;
using SlackNet.Events;

namespace Ananke.Platforms.Slack.Assistant;

/// <summary>
/// SlackNet <see cref="IEventHandler{T}"/> implementation that bridges
/// <see cref="AssistantThreadStarted"/> and <see cref="AssistantThreadContextChanged"/>
/// events from Slack's Agents &amp; AI Apps Assistant pane to the adapter's
/// bounded dispatch pipeline.
/// </summary>
internal sealed class SlackAssistantEventHandler(SlackAdapter adapter)
    : IEventHandler<AssistantThreadStarted>, IEventHandler<AssistantThreadContextChanged>
{
    public Task Handle(AssistantThreadStarted slackEvent)
    {
        adapter.EnqueueAssistantThread(slackEvent);
        return Task.CompletedTask;
    }

    public Task Handle(AssistantThreadContextChanged slackEvent)
    {
        adapter.EnqueueAssistantThread(slackEvent);
        return Task.CompletedTask;
    }
}
