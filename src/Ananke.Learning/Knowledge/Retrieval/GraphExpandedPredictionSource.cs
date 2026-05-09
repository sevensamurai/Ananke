using Ananke.Abstractions.Graph;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Knowledge.Builders;

namespace Ananke.Learning.Knowledge.Retrieval;

/// <summary>
/// <see cref="IPredictionSource"/> that seeds with tag-overlap neighbours, then
/// expands k hops through the tag graph to recover entries that pure vector recall
/// misses (multi-hop: tag A → tag B → tag C where the answer entry is tagged only
/// with C).
/// </summary>
/// <remarks>
/// <para>
/// Prediction is formed by weighting neighbour confidences by graph proximity:
/// direct tag-overlap neighbours contribute full weight; each additional hop
/// halves the contribution. This keeps the prediction anchored to structurally
/// close evidence without fully trusting distant nodes.
/// </para>
/// <para>
/// Falls back to <see langword="null"/> (leaving the decision to the caller) when
/// the graph is empty or no neighbours are reachable.
/// </para>
/// </remarks>
public sealed class GraphExpandedPredictionSource(
    IKnowledgeGraph graph,
    int neighborCount = 5,
    int hops = 2,
    int maxExpandNodes = 50) : IPredictionSource
{
    /// <inheritdoc />
    public async Task<float?> PredictAsync(
        EmpiricalEntry entry,
        IEmpiricalMemory memory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(memory);

        // Step 1: collect tag-node IDs from this entry's semantic tags.
        var tagSeeds = entry.Description.SemanticTags.Keys
            .Select(TagCoOccurrenceBuilder.TagId)
            .ToList();

        if (tagSeeds.Count == 0)
            return null;

        // Step 2: expand k hops through the tag graph.
        var expanded = await graph.ExpandAsync(tagSeeds, hops, maxExpandNodes, ct);
        if (expanded.Count == 0)
            return null;

        // Step 3: collect entry-node IDs reachable from the expanded tag nodes.
        var candidateEntryIds = new List<string>(expanded.Count);
        foreach (var node in expanded)
        {
            if (node.Kind != "entry") continue;
            // Node ID format: "entry:{empiricalEntryId}"
            candidateEntryIds.Add(node.Id["entry:".Length..]);
        }

        if (candidateEntryIds.Count == 0)
            return null;

        // Step 4: recall the actual entries and weight by hop distance.
        // We approximate hop distance by position in the BFS result list
        // (earlier = closer). Contribution halves every neighborCount positions.
        var weightedSum   = 0f;
        var totalWeight   = 0f;
        var retrieved     = 0;

        foreach (var candidateId in candidateEntryIds)
        {
            if (retrieved >= neighborCount) break;
            if (candidateId == entry.Id) continue;

            var candidate = await memory.GetAsync(candidateId, ct);
            if (candidate is null) continue;

            // Decay weight by position: weight = 1 / (1 + retrieved).
            var weight = 1f / (1f + retrieved);
            weightedSum += candidate.Confidence * weight;
            totalWeight += weight;
            retrieved++;
        }

        if (totalWeight <= 0f)
            return null;

        return weightedSum / totalWeight;
    }
}
