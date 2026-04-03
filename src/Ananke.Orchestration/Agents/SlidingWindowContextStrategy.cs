using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

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
/// var compacted = await strategy.ApplyAsync(messages, systemPrompt, ct);
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
    public Task<IReadOnlyList<AgentMessage>> ApplyAsync(
        IReadOnlyList<AgentMessage> messages,
        string? systemPrompt,
        CancellationToken ct = default)
    {
        if (messages.Count == 0)
            return Task.FromResult(messages);

        var systemTokens = systemPrompt is not null ? _tokenCounter.EstimateTokens(systemPrompt) : 0;
        var budget = _maxTokens - systemTokens;

        if (budget <= 0)
            // System prompt alone exceeds budget — keep only the last message
            return Task.FromResult<IReadOnlyList<AgentMessage>>([messages[^1]]);

        // Calculate total tokens and find how many messages to keep
        var messageCosts = new int[messages.Count];
        var total = 0;
        for (var i = 0; i < messages.Count; i++)
        {
            messageCosts[i] = _tokenCounter.EstimateTokens(messages[i]);
            total += messageCosts[i];
        }

        if (total <= budget)
            return Task.FromResult(messages);

        // Drop from the front until we fit. Always keep the last message.
        var dropIndex = 0;
        while (total > budget && dropIndex < messages.Count - 1)
        {
            total -= messageCosts[dropIndex];
            dropIndex++;
        }

        var result = new List<AgentMessage>(messages.Count - dropIndex);
        for (var i = dropIndex; i < messages.Count; i++)
            result.Add(messages[i]);

        return Task.FromResult<IReadOnlyList<AgentMessage>>(result);
    }
}
