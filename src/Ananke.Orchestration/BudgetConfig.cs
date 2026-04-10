using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration;

/// <summary>
/// Cost budget configuration for a workflow. When attached via
/// <see cref="Workflow{TState}.WithBudget(decimal)"/> or
/// <see cref="Workflow{TState}.WithBudget(decimal, decimal, decimal)"/>,
/// the runner tracks cumulative token usage across all LLM calls and terminates
/// the workflow when the estimated cost exceeds <see cref="MaxCost"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Multi-model workflows:</b> When models are selected via
/// <see cref="Agents.Routing.CapabilityModelRouter"/>, cost is computed per-call from the
/// selected <see cref="Agents.Routing.ModelProfile"/>'s rates. The flat rates on this class
/// serve as a fallback for models without profile cost data.
/// </para>
/// <para>
/// <b>Local models:</b> Models with zero cost (Ollama, llama.cpp, vLLM) are tracked
/// correctly — their calls don't contribute to cost but their tokens are still counted.
/// </para>
/// </remarks>
public sealed record BudgetConfig
{
    /// <summary>Maximum allowed estimated cost before the workflow is terminated.</summary>
    public required decimal MaxCost { get; init; }

    /// <summary>
    /// Fallback cost per 1,000 input (prompt) tokens, used when model-specific rates
    /// are not available. Defaults to <c>0</c>.
    /// </summary>
    public decimal CostPer1KInputTokens { get; init; }

    /// <summary>
    /// Fallback cost per 1,000 output (completion) tokens, used when model-specific rates
    /// are not available. Defaults to <c>0</c>.
    /// </summary>
    public decimal CostPer1KOutputTokens { get; init; }

    /// <summary>
    /// <c>true</c> when fallback cost rates are configured on this instance.
    /// </summary>
    internal bool HasFallbackRates => CostPer1KInputTokens != 0 || CostPer1KOutputTokens != 0;

    /// <summary>
    /// Calculates the estimated cost for the given <paramref name="usage"/> using
    /// the flat fallback rates on this config.
    /// </summary>
    public decimal EstimateCost(TokenUsage usage) =>
        (usage.InputTokens / 1000m * CostPer1KInputTokens) +
        (usage.OutputTokens / 1000m * CostPer1KOutputTokens);
}
