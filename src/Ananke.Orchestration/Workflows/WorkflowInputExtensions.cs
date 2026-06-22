using Ananke.Orchestration.Patterns;

namespace Ananke.Orchestration.Workflows;

/// <summary>
/// Channel-agnostic glue for resuming a paused <c>ask</c>/<see cref="Workflow{TState}.AwaitInput(string)"/>
/// turn from a platform adapter's inbound message.
/// </summary>
/// <remarks>
/// An <c>ask</c> turn is only useful if the adapter that owns a channel (Slack, Discord, a CLI
/// prompt, …) routes the next inbound message into a resume rather than treating it as a new
/// request or an approval reaction (ADR-arch-023 §4.1, §7). Correlating that inbound message to
/// the paused execution id — by conversation/thread id, stored alongside the paused execution —
/// is the adapter's job; this method only does the two steps every adapter would otherwise
/// repeat: fold the reply into state, then resume with the folded result.
/// </remarks>
public static class WorkflowInputExtensions
{
    /// <summary>
    /// Folds <paramref name="reply"/> into <paramref name="pausedState"/> via
    /// <paramref name="foldAnswer"/> — e.g. <see cref="Interview{TState}.FoldAnswer"/> —
    /// then resumes <paramref name="executionId"/> with the folded result.
    /// </summary>
    /// <param name="workflow">The workflow the paused execution belongs to.</param>
    /// <param name="executionId">The interrupted execution's id, as returned by the prior run/resume.</param>
    /// <param name="pausedState">
    /// The state observed when the execution paused (e.g. <c>execution.State</c> from the
    /// <see cref="ExecutionStatus.Interrupted"/> result) — the input the fold operates on.
    /// </param>
    /// <param name="reply">The user's free-text reply.</param>
    /// <param name="foldAnswer">Expand/skip/update (or any) fold from reply + state to next state.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<WorkflowExecution<TState>> ResumeWithInputAsync<TState>(
        this Workflow<TState> workflow,
        string executionId,
        TState pausedState,
        string reply,
        Func<TState, string, CancellationToken, Task<TState>> foldAnswer,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentNullException.ThrowIfNull(foldAnswer);

        var next = await foldAnswer(pausedState, reply, ct).ConfigureAwait(false);
        return await workflow.ResumeAsync(executionId, _ => next, ct).ConfigureAwait(false);
    }
}
