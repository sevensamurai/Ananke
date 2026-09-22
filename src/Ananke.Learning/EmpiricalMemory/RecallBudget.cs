namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// Spends a token allocation across a ranked set of matches, choosing how deep to go and — only if
/// that is not enough — how many to carry.
/// </summary>
/// <remarks>
/// <para>
/// Applied by every store after ranking, so recall behaves the same however it is backed. With no
/// budget set it is a pass-through and recall stays exactly as count-budgeted as it has always
/// been.
/// </para>
/// <para>
/// <b>Depth is spent before coverage.</b> Given a set that does not fit, this goes shallower on all
/// of it rather than dropping the tail — an unqualified partial view is the confident-false-belief
/// failure this tier exists to prevent, and it is much easier to produce by silently truncating a
/// list than by summarising one. Dropping entries happens only when even the cheapest rendering
/// does not fit, and what was dropped is reported so the omission is visible.
/// </para>
/// </remarks>
public static class RecallBudget
{
    /// <summary>Depths from richest to cheapest — the order the allocator gives ground in.</summary>
    private static readonly RecallDepth[] Descending =
        [RecallDepth.Full, RecallDepth.Entry, RecallDepth.Abstract];

    /// <summary>What one allocation decided.</summary>
    /// <param name="Matches">The matches to carry, each tagged with the depth to render it at.</param>
    /// <param name="Depth">The depth chosen, or <see langword="null"/> when no budget applied.</param>
    /// <param name="Considered">How many matches were ranked before the budget was applied.</param>
    /// <param name="EstimatedTokens">What the carried set is estimated to cost.</param>
    public readonly record struct Allocation(
        IReadOnlyList<EmpiricalMatch> Matches,
        RecallDepth? Depth,
        int Considered,
        int EstimatedTokens)
    {
        /// <summary>How many ranked matches did not fit and are not being carried.</summary>
        public int Omitted => Considered - Matches.Count;
    }

    /// <summary>Applies <paramref name="options"/> to an already-ranked, already-<c>TopK</c>-capped set.</summary>
    public static Allocation Apply(IReadOnlyList<EmpiricalMatch> ranked, RecallOptions options)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentNullException.ThrowIfNull(options);

        if (options.TokenBudget is not > 0)
            return new Allocation(ranked, options.Depth, ranked.Count, 0);

        var budget = options.TokenBudget.Value;
        var depths = options.Depth is { } fixedDepth ? [fixedDepth] : Descending;

        foreach (var depth in depths)
        {
            var cost = Cost(ranked, depth);
            if (cost <= budget)
                return new Allocation(Tag(ranked, depth), depth, ranked.Count, cost);
        }

        // Even the cheapest rendering of the whole set is too large: carry what fits, in rank order.
        var shallowest = depths[^1];
        var kept = new List<EmpiricalMatch>(ranked.Count);
        var spent = 0;

        foreach (var match in ranked)
        {
            var price = EmpiricalRecallRenderer.EstimateTokens(match, shallowest);
            if (spent + price > budget)
                break;

            kept.Add(match with { Depth = shallowest });
            spent += price;
        }

        return new Allocation(kept, shallowest, ranked.Count, spent);
    }

    private static int Cost(IReadOnlyList<EmpiricalMatch> matches, RecallDepth depth)
    {
        var total = 0;
        foreach (var match in matches)
            total += EmpiricalRecallRenderer.EstimateTokens(match, depth);

        return total;
    }

    private static IReadOnlyList<EmpiricalMatch> Tag(IReadOnlyList<EmpiricalMatch> matches, RecallDepth depth)
    {
        var tagged = new List<EmpiricalMatch>(matches.Count);
        foreach (var match in matches)
            tagged.Add(match with { Depth = depth });

        return tagged;
    }
}
