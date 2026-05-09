using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Graph.Algorithms;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Features;

namespace Ananke.Learning.Knowledge.Analytics;

/// <summary>
/// <see cref="ITagImportanceTracker"/> that ranks tags by PageRank on the tag
/// co-occurrence graph rather than by raw frequency.
/// </summary>
/// <remarks>
/// High-degree hub tags (e.g. very common ones like <c>type:error</c>) are penalised
/// by the PageRank damping factor; bridge tags that connect distinct topic clusters
/// score higher than frequency alone would suggest — making them better recall
/// discriminators.
/// </remarks>
public sealed class GraphTagImportanceTracker(
    IKnowledgeGraph graph,
    TagImportanceOptions? options = null) : ITagImportanceTracker
{
    private readonly TagImportanceOptions _options = options ?? new();
    private readonly PageRankCentralityScorer _scorer = new();

    /// <inheritdoc />
    public async Task<TagImportanceMap?> ComputeAsync(
        IEmpiricalMemory memory, CancellationToken ct = default)
    {
        // We need at least MinSampleSize entries to build a meaningful map.
        var total = await memory.CountAsync(ct: ct);
        if (total < _options.MinSampleSize)
            return null;

        // Score only "tag" nodes in the graph.
        var scores = await _scorer.ScoreAsync(graph, nodeKindFilter: "tag", ct);
        if (scores.Count == 0)
            return null;

        // Convert PageRank scores → importance dictionary.
        // PageRank is already in [0,1] range (each score is a probability mass).
        // Normalise so the max score maps to 1.0 to match TagImportanceMap convention.
        var maxScore = scores.Values.Max();
        if (maxScore <= 0f)
            return null;

        var importances = new Dictionary<string, float>(scores.Count);
        foreach (var (nodeId, score) in scores)
        {
            // Strip the "tag:" prefix to recover the original tag key.
            if (!nodeId.StartsWith("tag:", StringComparison.Ordinal)) continue;
            var tagKey = nodeId["tag:".Length..];
            importances[tagKey] = score / maxScore;
        }

        return new TagImportanceMap
        {
            Importances     = importances,
            EntriesAnalyzed = total,
            ComputedAt      = DateTimeOffset.UtcNow,
        };
    }
}
