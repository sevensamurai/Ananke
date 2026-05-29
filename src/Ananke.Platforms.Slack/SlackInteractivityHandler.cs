using SlackNet.Interaction;
using SlackNet.Interaction.Experimental;

namespace Ananke.Platforms.Slack;

/// <summary>
/// SlackNet <see cref="IAsyncBlockActionHandler"/> and <see cref="IAsyncViewSubmissionHandler"/>
/// implementation that routes block actions and view submissions through the adapter's
/// bounded dispatch pipeline.
/// </summary>
#pragma warning disable CS0618 // IAsyncBlockActionHandler / IAsyncViewSubmissionHandler are Experimental in SlackNet
internal sealed class SlackInteractivityHandler(SlackAdapter adapter)
    : IAsyncBlockActionHandler, IAsyncViewSubmissionHandler
{
    public Task Handle(BlockActionRequest request, Responder respond)
    {
        if (string.IsNullOrEmpty(request.User?.Id))
            return Task.CompletedTask;

        adapter.EnqueueInteraction(request);
        return Task.CompletedTask;
    }

    public Task Handle(ViewSubmission viewSubmission, Responder<ViewSubmissionResponse> respond)
    {
        adapter.EnqueueInteraction(viewSubmission);
        return Task.CompletedTask;
    }

    public Task HandleClose(ViewClosed viewClosed, Responder respond)
    {
        adapter.EnqueueInteraction(viewClosed);
        return Task.CompletedTask;
    }
}
#pragma warning restore CS0618
