namespace Ananke.Orchestration;

/// <summary>
/// Cost budget configuration for a workflow. When attached via
/// <see cref="Workflow{TState}.WithBudget"/>, the runner tracks cumulative
/// token usage across all LLM calls and terminates the workflow when
/// the estimated cost exceeds <see cref="MaxCost"/>.
/// </summary>
public sealed record BudgetConfig
{
    /// <summary>Maximum allowed estimated cost before the workflow is terminated.</summary>
    public required decimal MaxCost { get; init; }

    /// <summary>Cost per 1,000 input (prompt) tokens.</summary>
    public required decimal CostPer1KInputTokens { get; init; }

    /// <summary>Cost per 1,000 output (completion) tokens.</summary>
    public required decimal CostPer1KOutputTokens { get; init; }

    /// <summary>
    /// Calculates the estimated cost for the given <paramref name="usage"/>.
    /// </summary>
    public decimal EstimateCost(Agents.TokenUsage usage) =>
        (usage.InputTokens / 1000m * CostPer1KInputTokens) +
        (usage.OutputTokens / 1000m * CostPer1KOutputTokens);
}
