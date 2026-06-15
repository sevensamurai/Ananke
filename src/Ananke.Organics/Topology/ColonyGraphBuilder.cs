using Ananke.Abstractions.Graph;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;

namespace Ananke.Organics.Topology;

/// <summary>
/// Builds a colony graph from the live mesh state. Reads capability signals
/// from an <see cref="ICapabilityMap"/>, cell ancestry from an
/// <see cref="ILineageStore"/>, and optional routing weights from a
/// <see cref="RoutingAffinityTracker"/>.
/// </summary>
/// <remarks>
/// The builder is additive: it <em>upserts</em> nodes and edges into the
/// provided graph. Calling <see cref="BuildAsync"/> multiple times with the
/// same graph instance accumulates observations (edge weights are promoted via
/// the <c>IKnowledgeGraph</c> max-weight upsert rule). To obtain a fresh
/// snapshot, pass a new <see cref="InMemoryKnowledgeGraph"/>.
/// </remarks>
/// <param name="capabilityMap">Live capability landscape for the colony.</param>
/// <param name="lineageStore">Persistent cell birth/death records.</param>
/// <param name="affinityTracker">
/// Optional routing-affinity tracker. When supplied, learned routing weights
/// are projected as <c>routed_to</c> edges with <see cref="EdgeProvenance.Inferred"/>.
/// </param>
public sealed class ColonyGraphBuilder(
    ICapabilityMap capabilityMap,
    ILineageStore lineageStore,
    RoutingAffinityTracker? affinityTracker = null)
{
    // ── Node-ID helpers ──────────────────────────────────────────────

    internal static string CellId(string cellId) => $"cell:{cellId}";
    internal static string DomainId(string domain) => $"domain:{domain}";
    internal static string ToolId(string kit, string name) => $"tool:{kit}/{name}";

    // ── BuildAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Project the current mesh state into <paramref name="graph"/>.
    /// </summary>
    /// <param name="graph">Target graph. May already contain prior observations.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task BuildAsync(IKnowledgeGraph graph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        await AddCapabilityNodesAsync(graph, ct);
        await AddLineageEdgesAsync(graph, ct);
        await AddRoutingEdgesAsync(graph, ct);
    }

    // ── Private helpers ──────────────────────────────────────────────

    private async Task AddCapabilityNodesAsync(IKnowledgeGraph graph, CancellationToken ct)
    {
        var capabilities = capabilityMap.DiscoverAll();

        foreach (var cap in capabilities)
        {
            // Cell node
            await graph.UpsertNodeAsync(new GraphNode
            {
                Id = CellId(cap.WorkflowName),
                Kind = "cell",
                Properties = new Dictionary<string, string>
                {
                    ["name"] = cap.WorkflowName,
                    ["alive"] = cap.Alive.ToString(),
                    ["lastSensed"] = cap.LastSensed.ToString("O")
                }
            }, ct);

            // Domain node + serves edge
            await graph.UpsertNodeAsync(new GraphNode
            {
                Id = DomainId(cap.Domain),
                Kind = "domain",
                Properties = new Dictionary<string, string> { ["name"] = cap.Domain }
            }, ct);

            await graph.UpsertEdgeAsync(new GraphEdge
            {
                FromId = CellId(cap.WorkflowName),
                ToId = DomainId(cap.Domain),
                Relation = "serves",
                Provenance = EdgeProvenance.Extracted,
                Properties = new Dictionary<string, string> { ["source"] = "capability_map" }
            }, ct);

            // Tool nodes + serves edges from tool to cell
            foreach (var toolName in cap.Capabilities)
            {
                // Normalise: "kit/toolname" or just "toolname" → tool:unknown/toolname
                var (kit, name) = SplitTool(toolName);
                var toolNodeId = ToolId(kit, name);

                await graph.UpsertNodeAsync(new GraphNode
                {
                    Id = toolNodeId,
                    Kind = "tool",
                    Properties = new Dictionary<string, string>
                    {
                        ["kit"] = kit,
                        ["name"] = name
                    }
                }, ct);

                await graph.UpsertEdgeAsync(new GraphEdge
                {
                    FromId = CellId(cap.WorkflowName),
                    ToId = toolNodeId,
                    Relation = "uses",
                    Provenance = EdgeProvenance.Extracted,
                    Properties = new Dictionary<string, string> { ["source"] = "capability_map" }
                }, ct);
            }
        }
    }

    private async Task AddLineageEdgesAsync(IKnowledgeGraph graph, CancellationToken ct)
    {
        // Collect all cells we know about from generation 0 upward.
        // We iterate until an empty generation to avoid infinite loops.
        var seen = new HashSet<string>();
        int generation = 0;

        while (true)
        {
            var batch = await lineageStore.GetByGenerationAsync(generation, ct);
            if (batch.Count == 0) break;

            foreach (var lineage in batch)
            {
                if (!seen.Add(lineage.CellId)) continue;

                // Ensure the cell node exists (may not be alive any more)
                await graph.UpsertNodeAsync(new GraphNode
                {
                    Id = CellId(lineage.CellId),
                    Kind = "cell",
                    Properties = new Dictionary<string, string>
                    {
                        ["name"] = lineage.WorkflowName,
                        ["generation"] = lineage.Generation.ToString(),
                        ["alive"] = (lineage.DiedAt == null).ToString(),
                        ["bornAt"] = lineage.BornAt.ToString("O")
                    }
                }, ct);

                // Parent → child lineage edge
                if (lineage.ParentCellId != null)
                {
                    await graph.UpsertEdgeAsync(new GraphEdge
                    {
                        FromId = CellId(lineage.CellId),
                        ToId = CellId(lineage.ParentCellId),
                        Relation = "descended_from",
                        Provenance = EdgeProvenance.Extracted,
                        Properties = new Dictionary<string, string>
                        {
                            ["source"] = "lineage_store",
                            ["divisionReason"] = lineage.DivisionReason ?? string.Empty
                        }
                    }, ct);
                }
            }

            generation++;
        }
    }

    private async Task AddRoutingEdgesAsync(IKnowledgeGraph graph, CancellationToken ct)
    {
        if (affinityTracker is null) return;

        var affinities = affinityTracker.GetAffinities();

        foreach (var (cellName, (selections, meanReward, _)) in affinities)
        {
            if (selections == 0) continue;

            // We project affinity as domain→cell because the routing decision
            // starts from a domain lookup and ends at a cell.
            // The domain is not directly encoded in the affinity tracker, so we
            // emit a cell-level routing edge from a synthetic routing node.
            var routingNodeId = "routing:observed";

            await graph.UpsertNodeAsync(new GraphNode
            {
                Id = routingNodeId,
                Kind = "routing",
                Properties = new Dictionary<string, string> { ["note"] = "observed routing outcomes" }
            }, ct);

            await graph.UpsertEdgeAsync(new GraphEdge
            {
                FromId = routingNodeId,
                ToId = CellId(cellName),
                Relation = "routed_to",
                Provenance = EdgeProvenance.Inferred,
                Weight = Math.Max(0f, meanReward),
                Properties = new Dictionary<string, string>
                {
                    ["source"] = "routing_affinity_tracker",
                    ["selections"] = selections.ToString(),
                    ["meanReward"] = meanReward.ToString("F4")
                }
            }, ct);
        }
    }

    private static (string kit, string name) SplitTool(string toolName)
    {
        var slash = toolName.IndexOf('/');
        return slash > 0
            ? (toolName[..slash], toolName[(slash + 1)..])
            : ("unknown", toolName);
    }
}
