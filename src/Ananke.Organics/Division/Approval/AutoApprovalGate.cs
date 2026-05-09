namespace Ananke.Organics.Division.Approval;

/// <summary>
/// Default <see cref="IDivisionApprovalGate"/> that approves every proposed
/// division plan immediately. This preserves the current fully-automatic
/// division behaviour and serves as the zero-configuration default.
/// </summary>
public sealed class AutoApprovalGate : IDivisionApprovalGate
{
    /// <inheritdoc />
    public Task<DivisionApproval> ReviewAsync(
        DivisionPlan plan,
        ComplexitySnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return Task.FromResult(DivisionApproval.Approve(
            reason: "Auto-approved (no approval gate configured)",
            reviewedBy: "auto"));
    }
}
