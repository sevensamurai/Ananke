using Ananke.Platforms;
using Ananke.Roles.Studio;

namespace Ananke.Roles.Slack;

// TODO: The Slack-specific helpers in this namespace create a direct dependency between
// Ananke.Roles and Ananke.Platforms.Slack. In a future release, consider extracting these
// into a dedicated Ananke.Roles.Slack integration package so that Ananke.Roles can remain
// platform-agnostic and depend only on Ananke.Platforms abstractions.

/// <summary>
/// An <see cref="IPlatformMessageHandler"/> that resolves the agent role from
/// the incoming message's channel via a <see cref="SlackChannelMap"/>, then
/// delegates to a <see cref="StudioRouter"/> and invokes an
/// <see cref="OnWorkflowRoutedAsync"/> callback to execute the workflow.
/// </summary>
/// <remarks>
/// If the channel is not mapped, the handler falls back to
/// <paramref name="defaultWorkflowName"/>. Channel-name-to-role mappings are owned
/// by the caller's <see cref="StudioOptions"/> — no agency-specific names live
/// in this class.
/// </remarks>
public class RoleAwareMessageHandler(
    SlackChannelMap channelMap,
    StudioRouter router,
    string defaultWorkflowName) : IPlatformMessageHandler
{
    /// <summary>Workflow name used when the incoming channel is not in the channel map.</summary>
    public string DefaultWorkflowName => defaultWorkflowName;

    /// <inheritdoc />
    public async Task HandleAsync(PlatformMessage message, IPlatformResponseSink responseSink,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(responseSink);

        string workflowName;
        if (channelMap.TryResolveRole(message.ChannelId, out var role))
        {
            // Channel is mapped to a known role: the role name is the workflow target.
            workflowName = role!.Name;
        }
        else
        {
            // No channel mapping — delegate to the router with the configured default.
            var routed = await router.RouteAsync(
                message.Message.Content ?? string.Empty, ct).ConfigureAwait(false);
            workflowName = string.IsNullOrWhiteSpace(routed) ? defaultWorkflowName : routed;
        }

        await OnWorkflowRoutedAsync(message, responseSink, workflowName, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Called after the workflow name has been resolved. Override in a sub-class to
    /// execute the workflow. The default implementation is a no-op so that unit tests
    /// can verify routing without running a full workflow.
    /// </summary>
    protected virtual Task OnWorkflowRoutedAsync(
        PlatformMessage message,
        IPlatformResponseSink responseSink,
        string workflowName,
        CancellationToken ct) => Task.CompletedTask;
}
