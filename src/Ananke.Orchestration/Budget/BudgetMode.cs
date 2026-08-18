namespace Ananke.Orchestration.Budget;

/// <summary>
/// What happens when a workflow reaches its cost budget.
/// </summary>
/// <remarks>
/// A budget exists to stop a spike or a buggy workflow from producing an unexpected bill.
/// It is a guardrail, not cost accounting, and deliberately binds approximately — see
/// ADR-arch-028 Part C.
/// </remarks>
public enum BudgetMode
{
    /// <summary>
    /// Let the job(s) already in flight finish, launch nothing new, and end the execution
    /// with <see cref="Workflows.ExecutionStatus.BudgetExceeded"/>.
    /// </summary>
    /// <remarks>
    /// There is deliberately no <c>Cancel</c> mode. Cancelling a model call mid-flight does
    /// not save the money — the tokens are already committed and billed — while it does
    /// discard the answer just paid for. It would also manufacture in-flight context that a
    /// future resume could not replay.
    /// </remarks>
    Stop
}
