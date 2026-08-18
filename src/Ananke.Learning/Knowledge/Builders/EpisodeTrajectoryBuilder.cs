using Ananke.Abstractions.Graph;
using Ananke.Learning.Episodes;

namespace Ananke.Learning.Knowledge.Builders;

/// <summary>
/// Builds a trajectory graph from completed episodes.
/// Each episode produces <c>entry</c> nodes connected by <c>step_of</c> and
/// <c>follows</c> edges so that multi-step decision paths are queryable.
/// </summary>
/// <remarks>
/// <para>
/// Node ID conventions:
/// <list type="bullet">
///   <item><c>entry:{EpisodeStep.EntryId}</c></item>
///   <item><c>episode:{Episode.Id}</c></item>
/// </list>
/// </para>
/// <para>
/// Edge conventions:
/// <list type="bullet">
///   <item><c>step_of</c> (entry → episode) — <see cref="EdgeProvenance.Extracted"/></item>
///   <item><c>follows</c> (entry[n] → entry[n+1] within episode) — <see cref="EdgeProvenance.Extracted"/></item>
/// </list>
/// </para>
/// </remarks>
public sealed class EpisodeTrajectoryBuilder(IEpisodeStore episodeStore)
{
    /// <summary>
    /// Scans all episodes and upserts trajectory nodes and edges into
    /// <paramref name="graph"/>.
    /// </summary>
    public async Task BuildAsync(IKnowledgeGraph graph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var offset = 0;
        const int pageSize = 200;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await episodeStore.BrowseAsync(offset, pageSize, ct: ct).ConfigureAwait(false);
            if (page.Count == 0) break;

            foreach (var episode in page)
                await ProcessEpisodeAsync(graph, episode, ct).ConfigureAwait(false);

            offset += page.Count;
            if (page.Count < pageSize) break;
        }
    }

    private static async Task ProcessEpisodeAsync(
        IKnowledgeGraph graph, Episode episode, CancellationToken ct)
    {
        // Upsert episode node.
        await graph.UpsertNodeAsync(new GraphNode
        {
            Id = EpisodeId(episode.Id),
            Kind = "episode",
            Properties = new Dictionary<string, string>
            {
                ["terminal_reward"] = episode.TerminalReward.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            },
        }, ct).ConfigureAwait(false);

        string? previousEntryNodeId = null;

        foreach (var step in episode.Steps.OrderBy(s => s.StepIndex))
        {
            var entryNodeId = EntryId(step.EntryId);

            await graph.UpsertNodeAsync(new GraphNode { Id = entryNodeId, Kind = "entry" }, ct).ConfigureAwait(false);

            // entry → episode
            await graph.UpsertEdgeAsync(new GraphEdge
            {
                FromId = entryNodeId,
                ToId = EpisodeId(episode.Id),
                Relation = "step_of",
                Provenance = EdgeProvenance.Extracted,
            }, ct).ConfigureAwait(false);

            // entry[n-1] → entry[n]
            if (previousEntryNodeId is not null)
            {
                await graph.UpsertEdgeAsync(new GraphEdge
                {
                    FromId = previousEntryNodeId,
                    ToId = entryNodeId,
                    Relation = "follows",
                    Provenance = EdgeProvenance.Extracted,
                }, ct).ConfigureAwait(false);
            }

            previousEntryNodeId = entryNodeId;
        }
    }

    internal static string EpisodeId(string id) => $"episode:{id}";
    internal static string EntryId(string id) => $"entry:{id}";
}
