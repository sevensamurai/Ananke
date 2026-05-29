using SlackNet.Interaction;
using SlackNet.Interaction.Experimental;

namespace Ananke.Platforms.Slack;

/// <summary>
/// SlackNet <see cref="IAsyncSlashCommandHandler"/> that routes slash-command payloads
/// through the adapter's bounded dispatch pipeline.
/// </summary>
#pragma warning disable CS0618 // IAsyncSlashCommandHandler is marked Experimental in SlackNet
internal sealed class SlackSlashCommandHandler(SlackAdapter adapter)
    : IAsyncSlashCommandHandler
{
    public Task Handle(SlashCommand command, Responder<SlashCommandResponse> respond)
    {
        if (string.IsNullOrEmpty(command.UserId))
            return Task.CompletedTask;

        adapter.EnqueueSlashCommand(command);
        return Task.CompletedTask;
    }
}
#pragma warning restore CS0618
