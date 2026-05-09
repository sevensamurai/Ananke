namespace Ananke.Abstractions.Graph.Algorithms;

/// <summary>
/// <see cref="ICentralityScorer"/> that uses an iterative power-method PageRank.
/// Damping factor: <c>0.85</c>; convergence tolerance: <c>1e-6</c>; max iterations: <c>100</c>.
/// </summary>
public sealed class PageRankCentralityScorer : ICentralityScorer
{
    private const float DampingFactor = 0.85f;
    private const float Tolerance = 1e-6f;
    private const int MaxIterations = 100;

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, float>> ScoreAsync(
        IKnowledgeGraph graph,
        string? nodeKindFilter = null,
        CancellationToken ct = default)
    {
        if (graph is not InMemoryKnowledgeGraph inMemory)
            return new Dictionary<string, float>();

        // Collect all nodes (optionally filtered by kind).
        var nodeIds = new List<string>();
        foreach (var id in inMemory.AllNodeIds)
        {
            ct.ThrowIfCancellationRequested();
            if (nodeKindFilter is null)
            {
                nodeIds.Add(id);
            }
            else
            {
                var node = await graph.GetNodeAsync(id, ct).ConfigureAwait(false);
                if (node?.Kind == nodeKindFilter)
                    nodeIds.Add(id);
            }
        }

        var n = nodeIds.Count;
        if (n == 0)
            return new Dictionary<string, float>();

        var idToIndex = new Dictionary<string, int>(n);
        for (var i = 0; i < n; i++)
            idToIndex[nodeIds[i]] = i;

        // Build out-edge lists (within the filtered node set).
        var outNeighbors = new List<int>[n];
        for (var i = 0; i < n; i++)
            outNeighbors[i] = [];

        foreach (var id in nodeIds)
        {
            ct.ThrowIfCancellationRequested();
            var fromIdx = idToIndex[id];
            var edges = await graph.NeighborsAsync(id, ct: ct).ConfigureAwait(false);
            foreach (var edge in edges)
            {
                // Only consider outgoing edges (FromId == id) within the filtered set.
                if (edge.FromId == id && idToIndex.TryGetValue(edge.ToId, out var toIdx))
                    outNeighbors[fromIdx].Add(toIdx);
            }
        }

        var rank = new float[n];
        var newRank = new float[n];
        var initial = 1f / n;
        for (var i = 0; i < n; i++)
            rank[i] = initial;

        for (var iter = 0; iter < MaxIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            var danglingSum = 0f;
            for (var i = 0; i < n; i++)
                if (outNeighbors[i].Count == 0)
                    danglingSum += rank[i];

            var baseScore = (1f - DampingFactor) / n + DampingFactor * danglingSum / n;

            for (var j = 0; j < n; j++)
                newRank[j] = baseScore;

            for (var i = 0; i < n; i++)
            {
                if (outNeighbors[i].Count == 0) continue;
                var contribution = DampingFactor * rank[i] / outNeighbors[i].Count;
                foreach (var j in outNeighbors[i])
                    newRank[j] += contribution;
            }

            // Check convergence.
            var delta = 0f;
            for (var i = 0; i < n; i++)
                delta += Math.Abs(newRank[i] - rank[i]);

            Array.Copy(newRank, rank, n);

            if (delta < Tolerance)
                break;
        }

        var result = new Dictionary<string, float>(n);
        for (var i = 0; i < n; i++)
            result[nodeIds[i]] = rank[i];

        return result;
    }
}
