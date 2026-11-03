using Ananke.Abstractions.Tools;

namespace Ananke.Orchestration.Tools;

/// <summary>What happens when a model calls a tool its kit does not have.</summary>
internal static class UnknownTools
{
    /// <summary>
    /// Reports the call to <paramref name="observer"/> and the metric, and returns the error the model
    /// is answered with.
    /// </summary>
    internal static async Task<ToolResult> ReportAsync(
        IHallucinationObserver? observer,
        string requestedName,
        string? kitName,
        string agentId,
        string episodeId,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var evt = new HallucinatedToolCallEvent
        {
            RequestedToolName = requestedName,
            RequestedKitName = kitName,
            AgentId = agentId,
            EpisodeId = episodeId,
            OccurredAt = timeProvider.GetUtcNow(),
        };

        if (observer is not null)
            await observer.ReportAsync(evt, ct).ConfigureAwait(false);

        ToolMetrics.HallucinationReported.Add(1,
            new KeyValuePair<string, object?>("agent_id", agentId),
            new KeyValuePair<string, object?>("kit", kitName ?? string.Empty),
            new KeyValuePair<string, object?>("requested_name", requestedName));

        return ToolResult.Error(
            $"Unknown tool '{requestedName}': this tool is not registered. Do not call it again.");
    }
}
