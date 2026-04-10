using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Estimates tokens using the <c>chars / 4</c> heuristic. Suitable for most use
/// cases without a tokenizer dependency. Overestimates slightly for English text
/// and underestimates for CJK — acceptable for budget-gating where precision is
/// not critical.
/// </summary>
public sealed class ApproximateTokenCounter : ITokenCounter
{
    /// <summary>Shared singleton instance.</summary>
    public static ApproximateTokenCounter Instance { get; } = new();

    /// <inheritdoc />
    public int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4; // ceiling division

    /// <inheritdoc />
    public int EstimateTokens(AgentMessage message)
    {
        var total = 0;

        if (message.Content is not null)
            total += EstimateTokens(message.Content);

        if (message.Parts is { Count: > 0 })
        {
            foreach (var part in message.Parts)
            {
                if (part is TextPart tp)
                    total += EstimateTokens(tp.Text);
            }
        }

        if (message.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in message.ToolCalls)
                total += EstimateTokens(tc.FunctionName) + EstimateTokens(tc.Arguments);
        }

        return total;
    }
}
