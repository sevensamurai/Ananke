namespace Ananke.Organics.Division.Approval;

/// <summary>
/// Aggregated budget usage for a workflow or role across a time window.
/// </summary>
public sealed record BudgetSpend
{
    /// <summary>Input tokens consumed in the window.</summary>
    public required long TokensIn { get; init; }

    /// <summary>Output tokens consumed in the window.</summary>
    public required long TokensOut { get; init; }

    /// <summary>Estimated dollar cost accumulated in the window.</summary>
    public required decimal EstimatedUsd { get; init; }
}
