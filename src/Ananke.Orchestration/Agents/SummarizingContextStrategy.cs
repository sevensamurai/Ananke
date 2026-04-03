using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Context strategy that summarizes older messages via an LLM call when the
/// conversation history exceeds a token threshold. The summary replaces the
/// older messages with a single system-level summary message, preserving
/// recent context intact.
/// </summary>
/// <remarks>
/// <para>
/// When the total token count exceeds <see cref="ThresholdTokens"/>, the strategy:
/// </para>
/// <list type="number">
///   <item>Splits the message list into "old" (to summarize) and "recent" (to keep).</item>
///   <item>Calls the summarization model to produce a concise summary of the old messages.</item>
///   <item>Returns <c>[summary-message] + recent-messages</c>.</item>
/// </list>
/// <para>
/// When the total is under the threshold, the original list is returned unchanged.
/// The <see cref="RecentMessageCount"/> controls how many trailing messages are
/// always preserved (not summarized).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var strategy = new SummarizingContextStrategy(
///     summarizer: summaryModel,
///     thresholdTokens: 3000,
///     recentMessageCount: 4);
///
/// var compacted = await strategy.ApplyAsync(messages, systemPrompt, ct);
/// </code>
/// </example>
public sealed class SummarizingContextStrategy : IContextStrategy
{
    private readonly IAgentModel _summarizer;
    private readonly int _thresholdTokens;
    private readonly int _recentMessageCount;
    private readonly ITokenCounter _tokenCounter;
    private readonly string _summaryPrompt;

    /// <summary>The token threshold above which summarization is triggered.</summary>
    public int ThresholdTokens => _thresholdTokens;

    /// <summary>Number of recent messages always preserved (not summarized).</summary>
    public int RecentMessageCount => _recentMessageCount;

    /// <summary>
    /// Creates a summarizing context strategy.
    /// </summary>
    /// <param name="summarizer">
    /// The model used to generate the summary. Can be the same model as the main agent
    /// or a cheaper/faster model dedicated to summarization.
    /// </param>
    /// <param name="thresholdTokens">
    /// Token count above which summarization is triggered.
    /// When the total (system prompt + messages) is under this value, no summarization occurs.
    /// </param>
    /// <param name="recentMessageCount">
    /// Number of trailing messages to always preserve. Default is 4.
    /// These messages are never summarized, ensuring the most recent context is intact.
    /// </param>
    /// <param name="tokenCounter">
    /// Token estimator. Defaults to <see cref="ApproximateTokenCounter.Instance"/>.
    /// </param>
    /// <param name="summaryPrompt">
    /// Optional custom prompt for the summarization call. When <c>null</c>, a default
    /// prompt is used that asks for a concise factual summary.
    /// </param>
    public SummarizingContextStrategy(
        IAgentModel summarizer,
        int thresholdTokens,
        int recentMessageCount = 4,
        ITokenCounter? tokenCounter = null,
        string? summaryPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholdTokens, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recentMessageCount, 1);

        _summarizer = summarizer;
        _thresholdTokens = thresholdTokens;
        _recentMessageCount = recentMessageCount;
        _tokenCounter = tokenCounter ?? ApproximateTokenCounter.Instance;
        _summaryPrompt = summaryPrompt ??
            "Summarize the following conversation concisely. " +
            "Preserve key facts, decisions, and context needed to continue the conversation. " +
            "Do not add commentary — only summarize what was said.";
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentMessage>> ApplyAsync(
        IReadOnlyList<AgentMessage> messages,
        string? systemPrompt,
        CancellationToken ct = default)
    {
        if (messages.Count <= _recentMessageCount)
            return messages;

        var systemTokens = systemPrompt is not null ? _tokenCounter.EstimateTokens(systemPrompt) : 0;
        var total = systemTokens;
        for (var i = 0; i < messages.Count; i++)
            total += _tokenCounter.EstimateTokens(messages[i]);

        if (total <= _thresholdTokens)
            return messages;

        // Split: old messages to summarize, recent messages to keep
        var splitIndex = messages.Count - _recentMessageCount;
        var oldMessages = new List<AgentMessage>(splitIndex);
        for (var i = 0; i < splitIndex; i++)
            oldMessages.Add(messages[i]);

        var recentMessages = new List<AgentMessage>(_recentMessageCount);
        for (var i = splitIndex; i < messages.Count; i++)
            recentMessages.Add(messages[i]);

        // Build the summarization request
        var summaryInput = FormatMessagesForSummary(oldMessages);
        var summaryRequest = new AgentRequest
        {
            SystemPrompt = _summaryPrompt,
            Messages = [AgentMessage.User(summaryInput)]
        };

        var summaryResponse = await _summarizer.GenerateAsync(summaryRequest, ct);
        var summaryText = summaryResponse.Text ?? string.Empty;

        // Return: [summary as system-context message] + recent messages
        var result = new List<AgentMessage>(1 + recentMessages.Count);
        result.Add(AgentMessage.User($"[Previous conversation summary: {summaryText}]"));
        result.AddRange(recentMessages);

        return result;
    }

    private static string FormatMessagesForSummary(IReadOnlyList<AgentMessage> messages)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var msg in messages)
        {
            var role = msg.Role switch
            {
                AgentRole.User => "User",
                AgentRole.Assistant => "Assistant",
                AgentRole.Tool => "Tool",
                AgentRole.System => "System",
                _ => msg.Role.ToString()
            };
            sb.AppendLine($"{role}: {msg.Content ?? "(no text)"}");
        }
        return sb.ToString();
    }
}
