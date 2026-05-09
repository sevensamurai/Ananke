namespace Ananke.Organics.Division.Approval;

/// <summary>
/// Generic <see cref="IDivisionApprovalGate"/> backed by an async callback.
/// Use this to wire division approval to any external system — Slack, Teams,
/// email, a web dashboard, or a custom approval queue — without coupling
/// <c>Ananke.Organics</c> to platform-specific packages.
/// </summary>
/// <remarks>
/// <para>
/// Example: Slack human-in-the-loop approval using <c>Ananke.Platforms.Slack</c>:
/// </para>
/// <code>
/// var gate = new CallbackApprovalGate(async (plan, snapshot, ct) =>
/// {
///     // Post the plan summary to a Slack channel
///     var summary = $"🧬 Division proposed for *{plan.ParentWorkflow}*\n" +
///                   $"Reason: {plan.Reason}\n" +
///                   $"Children: {string.Join(", ", plan.Children.Select(c => c.Name))}";
///     var msgId = await slack.SendMessageAsync(approvalChannel, null, summary, ct);
///
///     // Wait for a human reaction (✅ = approve, ❌ = reject)
///     var reaction = await WaitForReactionAsync(approvalChannel, msgId, ct);
///
///     return reaction == "white_check_mark"
///         ? DivisionApproval.Approve("Human approved via Slack", reviewedBy: reactingUser)
///         : DivisionApproval.Reject("Human rejected via Slack", reviewedBy: reactingUser);
/// });
/// </code>
/// </remarks>
/// <param name="callback">
/// Async function that receives the proposed plan, complexity snapshot, and
/// cancellation token, and returns a <see cref="DivisionApproval"/>.
/// </param>
public sealed class CallbackApprovalGate(
    Func<DivisionPlan, ComplexitySnapshot, CancellationToken, Task<DivisionApproval>> callback)
    : IDivisionApprovalGate
{
    /// <inheritdoc />
    public Task<DivisionApproval> ReviewAsync(
        DivisionPlan plan,
        ComplexitySnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        return callback(plan, snapshot, ct);
    }
}
