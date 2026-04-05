using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Token consumption metadata returned by an LLM call.
/// </summary>
public sealed record TokenUsage
{
    /// <summary>Number of input/prompt tokens consumed.</summary>
    public int InputTokens { get; init; }

    /// <summary>Number of output/completion tokens generated.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Total tokens (input + output).</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// Returns a new <see cref="TokenUsage"/> that is the sum of this and <paramref name="other"/>.
    /// </summary>
    public TokenUsage Add(TokenUsage other) => new()
    {
        InputTokens = InputTokens + other.InputTokens,
        OutputTokens = OutputTokens + other.OutputTokens
    };

    /// <summary>Shared zero-value instance.</summary>
    public static TokenUsage Zero { get; } = new();
}

/// <summary>
/// AsyncLocal side-channel for <see cref="AgentJob{TState,TResponse}"/> to communicate
/// token usage back to <see cref="Execution.WorkflowRunner"/> without polluting user state.
/// Uses a mutable accumulator so writes in child execution contexts are visible to the parent.
/// </summary>
internal static class TokenUsageCapture
{
    internal static readonly AsyncLocal<UsageAccumulator?> Current = new();

    /// <summary>Accumulates usage from a response into the current capture.</summary>
    internal static void Accumulate(AgentResponse response)
    {
        if (response.Usage is null || Current.Value is null)
            return;

        Current.Value.Add(response.Usage);
    }

    /// <summary>
    /// Accumulates usage and computes per-call cost using model-specific rates.
    /// Called by <see cref="RoutedAgentModel"/> when the router implements
    /// <see cref="IModelCostResolver"/>.
    /// </summary>
    internal static void AccumulateWithCost(AgentResponse response, ModelCostRates rates)
    {
        if (response.Usage is null || Current.Value is null)
            return;

        Current.Value.Add(response.Usage);
        Current.Value.AddCost(rates.EstimateCost(response.Usage));
    }
}

/// <summary>Mutable accumulator shared by reference through the AsyncLocal.</summary>
internal sealed class UsageAccumulator
{
    public TokenUsage Usage { get; private set; } = TokenUsage.Zero;

    /// <summary>
    /// Cost accumulated from model-specific rates. When <c>&gt; 0</c>, the runner
    /// uses this instead of flat <see cref="BudgetConfig"/> rates for budget enforcement.
    /// </summary>
    public decimal AccumulatedCost { get; private set; }

    /// <summary>Whether any per-call model-based cost has been reported.</summary>
    public bool HasModelBasedCost { get; private set; }

    public void Add(TokenUsage usage) =>
        Usage = Usage.Add(usage);

    public void AddCost(decimal cost)
    {
        AccumulatedCost += cost;
        HasModelBasedCost = true;
    }
}
