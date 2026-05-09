namespace Ananke.Organics.Division.Approval;

/// <summary>
/// The outcome of an <see cref="IDivisionApprovalGate"/> review. A gate can
/// approve the plan as-is, reject it entirely, or revise it (approve with
/// modifications to children, tools, or reason).
/// </summary>
public sealed record DivisionApproval
{
    /// <summary>Whether the division is approved to proceed.</summary>
    public required bool IsApproved { get; init; }

    /// <summary>
    /// When the reviewer modifies the plan (e.g. removes a child, reassigns tools),
    /// this contains the revised plan. <see langword="null"/> when the original
    /// plan is approved without changes or when rejected.
    /// </summary>
    public DivisionPlan? RevisedPlan { get; init; }

    /// <summary>Human- or LLM-readable explanation for the decision.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Identifier of the reviewer — a user name, Slack user ID, model name, or
    /// <c>"auto"</c> for the default gate.
    /// </summary>
    public string? ReviewedBy { get; init; }

    /// <summary>When the review was completed.</summary>
    public DateTimeOffset ReviewedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Create an approval that accepts the plan as-is.</summary>
    public static DivisionApproval Approve(string reason, string? reviewedBy = null) => new()
    {
        IsApproved = true,
        Reason = reason,
        ReviewedBy = reviewedBy
    };

    /// <summary>Create a rejection that blocks the division.</summary>
    public static DivisionApproval Reject(string reason, string? reviewedBy = null) => new()
    {
        IsApproved = false,
        Reason = reason,
        ReviewedBy = reviewedBy
    };

    /// <summary>Create an approval with a revised plan.</summary>
    public static DivisionApproval Revise(DivisionPlan revisedPlan, string reason, string? reviewedBy = null) => new()
    {
        IsApproved = true,
        RevisedPlan = revisedPlan,
        Reason = reason,
        ReviewedBy = reviewedBy
    };
}
