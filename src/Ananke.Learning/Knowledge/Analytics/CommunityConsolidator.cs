using Ananke.Abstractions.Graph;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Offline;
using Ananke.Orchestration.Knowledge;

namespace Ananke.Learning.Knowledge.Analytics;

/// <summary>
/// Decorator around <see cref="IConsolidationSummarizer"/> that uses
/// <see cref="ICommunityDetector"/> (when registered) as a hint before
/// delegating to the inner summarizer.
/// </summary>
/// <remarks>
/// <para>
/// When an <see cref="ICommunityDetector"/> is injected the decorator records
/// the detected community label as metadata on the returned
/// <see cref="KnowledgeDocument"/> under the key <c>graph_community</c>.
/// The offline learner can use this label to pick one representative entry
/// per community during consolidation, replacing recency-only selection.
/// </para>
/// <para>
/// When no <see cref="ICommunityDetector"/> is registered the decorator is
/// transparent: it calls the wrapped summarizer unchanged.
/// </para>
/// </remarks>
public sealed class CommunityConsolidator(
    IConsolidationSummarizer inner,
    IKnowledgeGraph graph,
    ICommunityDetector? detector = null) : IConsolidationSummarizer
{
    /// <inheritdoc />
    public async Task<KnowledgeDocument> SummarizeAsync(
        EmpiricalEntry entry, CancellationToken ct = default)
    {
        var doc = await inner.SummarizeAsync(entry, ct);

        if (detector is null)
            return doc;

        var communities = await detector.DetectAsync(graph, ct);

        var entryNodeId = $"entry:{entry.Id}";
        if (!communities.TryGetValue(entryNodeId, out var communityLabel))
            return doc;

        // Merge the community label into the document metadata.
        var merged = new Dictionary<string, string>(doc.Metadata)
        {
            ["graph_community"] = communityLabel.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        return doc with { Metadata = merged };
    }
}
