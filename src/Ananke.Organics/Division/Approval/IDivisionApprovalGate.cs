namespace Ananke.Organics.Division.Approval;

/// <summary>
/// Reviews a proposed <see cref="DivisionPlan"/> before execution and decides
/// whether to approve, reject, or revise it. This is the governance checkpoint
/// between <see cref="IDivisionPolicy"/> (which proposes) and
/// <see cref="IWorkflowDivider"/> (which executes).
/// </summary>
/// <remarks>
/// <para>
/// The default implementation (<see cref="AutoApprovalGate"/>) approves all plans
/// immediately — preserving the current fully-automatic behaviour. Swap in
/// <see cref="LlmApprovalGate"/> for autonomous LLM supervision or
/// <see cref="CallbackApprovalGate"/> for human-in-the-loop workflows
/// (Slack, Teams, email, web UI, etc.).
/// </para>
/// <para>
/// Gates compose: chain multiple gates by nesting decorators or by implementing
/// a composite that runs gates in sequence, short-circuiting on the first rejection.
/// </para>
/// </remarks>
public interface IDivisionApprovalGate
{
    /// <summary>
    /// Review the proposed division plan. Return <see cref="DivisionApproval"/>
    /// indicating whether to proceed, reject, or revise.
    /// </summary>
    /// <param name="plan">The division plan proposed by the policy.</param>
    /// <param name="snapshot">The complexity snapshot that triggered the proposal.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DivisionApproval> ReviewAsync(
        DivisionPlan plan,
        ComplexitySnapshot snapshot,
        CancellationToken ct = default);
}
