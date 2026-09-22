using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>Why part of the history is not in the projection the model saw.</summary>
public enum ContextShadowReason
{
    /// <summary>Nothing was withheld.</summary>
    None = 0,

    /// <summary>Messages were dropped outright.</summary>
    Dropped,

    /// <summary>Messages were replaced by a generated summary.</summary>
    Summarized
}

/// <summary>
/// What a strategy decided the model should see, together with what it withheld.
/// </summary>
/// <remarks>
/// Compaction used to return a plain message list, so what a strategy dropped was simply gone and
/// "what did the model actually see at step N" was unanswerable after the fact. The shadow counts
/// here are what make that answerable — deliberately counts and a reason rather than the dropped
/// messages themselves, so reporting never pins the very content compaction just released.
/// </remarks>
public sealed record ContextProjection
{
    /// <summary>The messages to send.</summary>
    public required IReadOnlyList<AgentMessage> Messages { get; init; }

    /// <summary>How many messages the model did not see.</summary>
    public int ShadowedCount { get; init; }

    /// <summary>Estimated size of what the model did not see.</summary>
    public int ShadowedTokens { get; init; }

    /// <summary>Why they were withheld.</summary>
    public ContextShadowReason Reason { get; init; }

    /// <summary>The token budget actually enforced, after allocation and capacity were reconciled.</summary>
    public int AppliedBudget { get; init; }

    /// <summary>Whether anything was withheld at all.</summary>
    public bool Compacted => ShadowedCount > 0;

    /// <summary>A projection in which nothing was withheld.</summary>
    public static ContextProjection Unchanged(IReadOnlyList<AgentMessage> messages, int appliedBudget) =>
        new() { Messages = messages, AppliedBudget = appliedBudget };
}
