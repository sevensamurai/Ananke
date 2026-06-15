namespace Ananke.Abstractions.Budget;

/// <summary>
/// Reads current budget usage for a workflow or role.
/// </summary>
public interface IBudgetMeter
{
    /// <summary>
    /// Gets the current budget usage for the specified workflow or role key.
    /// </summary>
    /// <param name="role">Workflow or role identifier to query.</param>
    BudgetSpend GetCurrentSpend(string role);

    /// <summary>
    /// Determines whether the current total token usage is at or above the supplied cap.
    /// </summary>
    /// <param name="role">Workflow or role identifier to query.</param>
    /// <param name="cap">Token cap for the rolling window.</param>
    bool IsOverCap(string role, long cap);
}
