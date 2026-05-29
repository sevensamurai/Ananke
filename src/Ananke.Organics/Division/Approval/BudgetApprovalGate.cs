namespace Ananke.Organics.Division.Approval;

/// <summary>
/// <see cref="IDivisionApprovalGate"/> that blocks division when a workflow exceeds a token cap.
/// </summary>
public sealed class BudgetApprovalGate(
    IBudgetMeter budgetMeter,
    long tokenCap) : IDivisionApprovalGate
{
    /// <inheritdoc />
    public Task<DivisionApproval> ReviewAsync(
        DivisionPlan plan,
        ComplexitySnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(tokenCap);

        var role = plan.ParentWorkflow;
        var spend = budgetMeter.GetCurrentSpend(role);
        var totalTokens = spend.TokensIn + spend.TokensOut;

        if (budgetMeter.IsOverCap(role, tokenCap))
        {
            return Task.FromResult(DivisionApproval.Reject(
                reason: $"Division blocked: {role} has consumed {totalTokens} tokens in the current window, meeting or exceeding cap {tokenCap}.",
                reviewedBy: "budget-meter"));
        }

        return Task.FromResult(DivisionApproval.Approve(
            reason: $"Budget within cap: {role} has consumed {totalTokens} tokens in the current window against cap {tokenCap}.",
            reviewedBy: "budget-meter"));
    }
}
