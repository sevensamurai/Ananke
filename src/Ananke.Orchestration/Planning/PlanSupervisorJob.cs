using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tracing;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// The job that decides what a halted plan becomes, and applies the decision.
/// </summary>
/// <remarks>
/// <para>
/// <b>A peer of the supervised job, not something inside it.</b> Deciding that a plan is wrong is a
/// judgement above the pass that discovered it, and the framework already expresses "run, judge, run
/// again" as two jobs and a <c>Loop</c>. Putting the decision in a job of its own is what lets a
/// consumer interrupt before it, checkpoint around it, see it in the run's history and topology, and
/// replace it with something that is not a model at all.
/// </para>
/// <para>
/// <b>It applies the decision itself.</b> Re-ruling goes through the supervision, so the version is
/// minted against the same store the supervised job reads — not a second one assembled from the same
/// options.
/// </para>
/// <para>
/// <b>It is not the node resolving its own dispute.</b> This is supplied by whoever composed the
/// plan, exactly as the verifier is, and a node can neither see nor reach it.
/// </para>
/// <para>
/// <b>On a round the run paused for, the answer already in state is the decision</b> and the
/// coordinator is not asked. That is read from how the job was entered — the runner reports the
/// arrival it was resumed into — and it is the framework's to observe rather than an obligation on
/// whoever wrote the coordinator: a coordinator that decided for itself here would overwrite what
/// somebody was stopped and asked for, and the record would then attribute its answer to them. The
/// decision is still applied, reported and left uncharged exactly as any other, so a person's
/// re-ruling mints its version through the same call.
/// </para>
/// </remarks>
/// <param name="name">The job's name, as it appears in history, topology and <c>InterruptBefore</c>.</param>
/// <param name="supervision">The supervision whose store and clock a re-ruling is minted against.</param>
/// <param name="coordinate">Who decides.</param>
/// <param name="read">Where the last pass's outcome is in state.</param>
/// <param name="write">How to put the decision back.</param>
public sealed class PlanSupervisorJob<TState>(
    string name,
    SupervisionOptions supervision,
    PlanSupervisor coordinate,
    Func<TState, PlanCoordination?> read,
    Func<TState, PlanCoordination, TState> write) : IJob<TState>
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        // A settled plan has nothing to decide. The job still runs — it is on the main line — and
        // returning the state untouched is what lets the loop's predicate see that and exit.
        if (read(state) is not { } coordination || coordination.Result.HaltedAt is not { } haltedAt)
            return state;

        // Whether this round is one the run paused for, taken from the runner rather than declared:
        // a decision reached this way was reached on an answer from outside the loop, so the budget
        // that bounds the loop's own re-planning has nothing to say about it.
        var escalated = WorkflowTraceContext.Value is { ResumedInto: true };

        // An answer that came from outside the run is the answer, and the coordinator is not asked to
        // second-guess it. Asking anyway is not a missed optimisation: the reply would overwrite what
        // was decided while the run was paused, and the record would then report a coordinator's own
        // decision as one somebody was consulted for — escalated, and outside the change-of-plan
        // budget. A coordinator that wants to weigh a proposal is a coordinator wired to pause
        // between proposing and deciding, which is a topology, not a signature.
        //
        // Resuming with nothing decided still asks: somebody looked and did not answer, which is not
        // the same as nobody having been asked. The round stays escalated either way — the pause is
        // what bounded it, not who filled the slot.
        var decision = escalated && coordination.Decision is { } answered
            ? answered
            : await coordinate(coordination, ct).ConfigureAwait(false);

        // Said before it is acted on, so a reader sees the decision and then its consequences rather
        // than a version appearing with nothing having asked for it.
        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanDecisionTaken
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = coordination.Result.Tree.PlanId,
            PlanVersion = coordination.Result.Tree.Current.Number,
            NodeId = haltedAt,
            Decision = decision,
            Changes = coordination.Changes,
            MaxChanges = coordination.MaxChanges,
            Escalated = escalated
        }, ct).ConfigureAwait(false);

        // A person giving a step up now calls AbandonAsync directly — the same door DisputeAsync and
        // AskAsync already use — rather than going through a PlanDecision shape of its own; GiveUp
        // folds into Ask/Replan in the vocabulary (R30), not into a third thing this job applies. A
        // replan with no contract is the Planner's to write — this delegate-coordinator form has no
        // Planner downstream of it, so there is nothing to apply here.
        if (decision is PlanDecision.ReplanPlan { Contract: { } contract } rerule)
        {
            await supervision.ReruleAsync(
                coordination.Result.Tree.PlanId,
                // Not necessarily the node that halted: a contradiction surfaces in a child and the
                // contract that has to change is often the parent's, because a node's input is the
                // previous node's output.
                rerule.NodeId ?? haltedAt,
                contract,
                // Taken from the halt, never from the decision. The coordinator did not witness what
                // went wrong, so the version records what did — and whatever the coordinator
                // concluded travels beside it, attributed, rather than in place of it.
                coordination.HaltReason
                    ?? $"'{haltedAt}' stopped, and nothing recorded why.",
                rerule.Children,
                rerule.Rationale,
                ct).ConfigureAwait(false);
        }
        // An Ask mints no version and spends no budget: from this seat it is the resolved answer —
        // there is no chooser downstream of a delegate coordinator to put it to, unlike PlanAskingJob's
        // pipeline where PlanChoiceJob is what turns Question into a decision. Writing one here would
        // leave it permanently outstanding (nothing ever sets Picked or Refused on it) and the router
        // reads outstanding-with-no-advisor as "ask again" — a halt this seat meant to end the run on
        // would loop back onto itself forever instead. Question is therefore left exactly as it was.

        return write(state, coordination with
        {
            Decision = decision,
            // Stopping spends nothing: it ends the run rather than buying another pass. Nor does an
            // escalated answer — the budget bounds the re-planning this loop does by itself, and a
            // round it paused for is bounded by somebody being there to answer.
            Changes = coordination.Changes + (PlanDecision.Spends(decision, escalated) ? 1 : 0)
        });
    }
}
