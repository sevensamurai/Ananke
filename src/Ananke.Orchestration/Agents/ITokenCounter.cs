using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Estimates the token count for text content. Used by context strategies
/// to determine when compaction is needed.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Estimates the token count for a single text string.</summary>
    int EstimateTokens(string text);

    /// <summary>Estimates the total token count for a message (all content parts).</summary>
    int EstimateTokens(AgentMessage message);
}
