using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Context strategy that keeps the most recent messages within a token budget.
/// Drops oldest messages first, always preserving the last user message.
/// </summary>
/// <remarks>
/// <para>
/// When the total token count of the system prompt plus all messages exceeds
/// <see cref="MaxTokens"/>, the strategy removes messages from the beginning
/// of the list (oldest first) until the budget is satisfied. The last message
/// in the list is always preserved regardless of budget — it is assumed to be
/// the current user turn.
/// </para>
/// <para>
/// This strategy is stateless and does not modify the original list. A new
/// list is returned only when compaction is needed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var strategy = new SlidingWindowContextStrategy(
///     maxTokens: 4096,
///     tokenCounter: ApproximateTokenCounter.Instance);
///
/// var projection = await strategy.ApplyAsync(messages, systemPrompt, ContextBudget.Unspecified, ct);
/// </code>
/// </example>
public sealed class SlidingWindowContextStrategy : IContextStrategy
{
    private readonly int _maxTokens;
    private readonly ITokenCounter _tokenCounter;

    /// <summary>The token budget this strategy enforces.</summary>
    public int MaxTokens => _maxTokens;

    /// <summary>
    /// Creates a sliding window strategy.
    /// </summary>
    /// <param name="maxTokens">Maximum allowed tokens (system prompt + messages).</param>
    /// <param name="tokenCounter">Token estimator. Defaults to <see cref="ApproximateTokenCounter.Instance"/>.</param>
    public SlidingWindowContextStrategy(int maxTokens, ITokenCounter? tokenCounter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 1);
        _maxTokens = maxTokens;
        _tokenCounter = tokenCounter ?? ApproximateTokenCounter.Instance;
    }

    /// <inheritdoc />
    public Task<ContextProjection> ApplyAsync(
        IReadOnlyList<AgentMessage> messages,
        string? systemPrompt,
        ContextBudget budget,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(budget);

        // The caller's allocation wins over the configured one, and the model's real window caps
        // both — so this instance can serve a router whose candidates have different windows.
        var applied = budget.Resolve(_maxTokens);

        if (messages.Count == 0)
            return Task.FromResult(ContextProjection.Unchanged(messages, applied));

        var systemTokens = systemPrompt is not null ? _tokenCounter.EstimateTokens(systemPrompt) : 0;
        var remaining = applied - systemTokens;

        var messageCosts = new int[messages.Count];
        var total = 0;
        for (var i = 0; i < messages.Count; i++)
        {
            messageCosts[i] = _tokenCounter.EstimateTokens(messages[i]);
            total += messageCosts[i];
        }

        if (remaining <= 0)
        {
            // System prompt alone exceeds the budget — keep only the last message.
            var shadowed = 0;
            for (var i = 0; i < messages.Count - 1; i++)
                shadowed += messageCosts[i];

            return Task.FromResult(new ContextProjection
            {
                Messages = [messages[^1]],
                ShadowedCount = messages.Count - 1,
                ShadowedTokens = shadowed,
                Reason = messages.Count > 1 ? ContextShadowReason.Dropped : ContextShadowReason.None,
                AppliedBudget = applied
            });
        }

        if (total <= remaining)
            return Task.FromResult(ContextProjection.Unchanged(messages, applied));

        // Drop from the front until we fit. Always keep the last message.
        var dropIndex = 0;
        var droppedTokens = 0;
        while (total > remaining && dropIndex < messages.Count - 1)
        {
            total -= messageCosts[dropIndex];
            droppedTokens += messageCosts[dropIndex];
            dropIndex++;
        }

        var result = new List<AgentMessage>(messages.Count - dropIndex);
        for (var i = dropIndex; i < messages.Count; i++)
            result.Add(messages[i]);

        return Task.FromResult(new ContextProjection
        {
            Messages = result,
            ShadowedCount = dropIndex,
            ShadowedTokens = droppedTokens,
            Reason = dropIndex > 0 ? ContextShadowReason.Dropped : ContextShadowReason.None,
            AppliedBudget = applied
        });
    }
}
