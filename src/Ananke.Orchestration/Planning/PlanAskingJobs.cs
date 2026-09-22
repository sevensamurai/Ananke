using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Streaming;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// Asks the advisor what a halt admits, and records the question for somebody to answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first half of asking, and it ships because everybody was writing it.</b> Producing
/// alternatives is a model's work; putting them where a host can render them, appending the way out,
/// and answering on behalf of an unattended run are not judgements at all — they are the same four
/// lines in every consumer, and the fourth is the one that gets forgotten.
/// </para>
/// <para>
/// <b>Autopilot answers here, not at a pause.</b> A pause hands control to something outside the run;
/// an unattended run has no outside, so it takes the recommendation as it goes and never stops. That
/// is also what makes the round charged like any other the run took by itself — nothing declares it,
/// because nothing paused.
/// </para>
/// <para>
/// <b>A settled plan is not asked about</b>, and neither is a halt the advisor had nothing to say
/// about: no question is recorded, and whatever routes on the question sees there is none.
/// </para>
/// </remarks>
/// <param name="name">The job's name, as it appears in history and topology.</param>
/// <param name="advisor">Who offers the alternatives.</param>
/// <param name="supervision">
/// The supervision whose store a term is committed against — a term outlives this halt, so it is
/// written where the plan lives rather than carried in workflow state.
/// </param>
/// <param name="read">Where the plan's progress is in state.</param>
/// <param name="write">How to put it back.</param>
/// <param name="autopilot">
/// Whether the run answers its own questions with the recommendation. Owned by whoever builds the
/// workflow, because it is a property of the run rather than of any one halt.
/// </param>
public sealed class PlanAskingJob<TState>(
    string name,
    PlanAdvisor advisor,
    SupervisionOptions supervision,
    Func<TState, PlanCoordination?> read,
    Func<TState, PlanCoordination, TState> write,
    bool autopilot = false) : IJob<TState>
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        if (read(state) is not { } coordination || coordination.Result.HaltedAt is null)
            return state;

        // Nothing to ask, and asking anyway costs a call that cannot succeed. The attempt died of
        // something no seat in this run can answer — an account out of allowance is the case — and
        // this seat runs on the same credentials as the one that just failed. Returning untouched
        // leaves no question, which the topology already reads as a run that ends here; what it must
        // not do is escalate into more of the same and then report the silence as nobody having had
        // anything to suggest.
        if (coordination.Unanswerable)
            return state;

        var proposal = await advisor(coordination, ct).ConfigureAwait(false);

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanProposalOffered
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = coordination.Result.Tree.PlanId,
            PlanVersion = coordination.Result.Tree.Current.Number,
            NodeId = coordination.NodeId!,
            HaltReason = coordination.HaltReason,
            Options = proposal.Options,
            Discarded = proposal.Discarded,
            Exhausted = proposal.Exhausted
        }, ct).ConfigureAwait(false);

        // What the supervisor read as settled rather than as an answer, committed before anything is
        // offered — it binds whatever is authored next, including the options just proposed, and a
        // term applied only after the round it was read in would miss the change of plan it exists to
        // shape. The person's own sentence goes with it, never replaced by the reading of it.
        foreach (var term in proposal.Terms)
        {
            // A term the supervisor adopted from what it was shown as *remembered* keeps the words
            // and the attribution it arrived with: whoever reads the record afterwards has to be able
            // to see that it was first said in another run, by somebody answering a different
            // question. Replacing that with this run's answerer would turn a proposal somebody
            // accepted into a statement nobody made.
            var adopted = coordination.Recalled.FirstOrDefault(
                r => string.Equals(r.Reading, term, StringComparison.OrdinalIgnoreCase));

            if ((adopted?.Said ?? coordination.Said) is not { } said)
                continue;

            await supervision.CommitAsync(
                coordination.Result.Tree.PlanId,
                term,
                said,
                adopted?.By ?? PlanCoordination.Answerer,
                ct: ct).ConfigureAwait(false);
        }

        // Nothing offered is not an empty question. A question with no options asks somebody to
        // choose between nothing and cancelling, which is not a choice — so none is recorded, and
        // the run falls through to whatever it does when nobody can suggest anything.
        if (proposal.Empty)
        {
            // What it tried and why none of it could be used, carried for whoever is asked next.
            // A seat handed a halt with no record of the last attempt is being asked to guess
            // differently; every guard here already wrote down the shape it would have accepted.
            return write(state, coordination with
            {
                Question = null,
                Said = null,
                Refused = proposal.Exhausted ? [] : proposal.Discarded
            });
        }

        var question = new PlanQuestion { Options = proposal.Options };

        if (autopilot)
        {
            var recommended = proposal.Options.ToList().FindIndex(o => o.Recommended);
            question = question with { Picked = (recommended < 0 ? 0 : recommended) + 1 };
        }

        // Cleared once it has been asked with: the advisor above has just seen it and answered on the
        // strength of it, and a free-text answer left standing would be handed to every later round
        // as though it had never been read — the same sentence steering a question it was never
        // about. What it produced is in the options; the sentence itself is in the record.
        return write(state, coordination with { Question = question, Said = null });
    }
}

/// <summary>
/// Applies whatever was chosen: gives the step up, answers it, or signals the Planner to replan.
/// </summary>
/// <remarks>
/// <para>
/// <b>The second half, and the one with the guarantee in it.</b> An outstanding question returns the
/// state untouched, which is what lets the topology send it back to be asked again — <em>not
/// selecting is not an option</em>, enforced by where the run goes rather than by anything this job
/// checks.
/// </para>
/// <para>
/// <b>Cancelling is an answer.</b> It ends the run with the plan unsettled, recorded as a decision
/// rather than as a fault, because a plan nobody could fix is a real outcome.
/// </para>
/// <para>
/// <b>Wrap it to have the accounting done for you.</b> Like any coordinator job it decides and
/// applies but does not account; <c>AccountedSupervisorJob</c> is what spends the budget and reports
/// the decision, and a hand-wired workflow has to ask for that explicitly.
/// </para>
/// </remarks>
public sealed class PlanChoiceJob<TState>(
    string name,
    SupervisionOptions supervision,
    Func<TState, PlanCoordination?> read,
    Func<TState, PlanCoordination, TState> write) : IJob<TState>
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        if (read(state) is not { } coordination || coordination.Result.HaltedAt is not { } haltedAt)
            return state;

        if (coordination.Question is not { } question)
            return state;

        // Nobody has answered. Nothing is decided, nothing is reported, nothing is spent — and the
        // run goes back to the question rather than past it.
        if (question.Outstanding)
            return state;

        // An answer nobody listed is not a choice to apply, and nothing here can read prose. It goes
        // back to the seat that can: the supervisor authors a fresh set of options with it in hand,
        // reached through the same job that asked the first time. The halt is deliberately
        // left standing — the step is still unsatisfied, and this decided nothing about it.
        if (question.Said is { } said)
            return write(state, coordination with
            {
                Said = said,
                Question = null
            });

        // Cancelling is a person's, is nobody's to author, and ends the run — so an Ask with nothing
        // left to offer and no outstanding question is what the topology reads as it ending here.
        if (question.Refused is PlanRefusal.Cancel || question.Chosen is not { } chosen)
            return write(state, coordination with
            {
                Decision = PlanDecision.Ask([]),
                Question = null
            });

        // Giving the step up is not stopping, and it is no longer a refusal either: it is an
        // authored option, so it arrives picked rather than refused into, having been weighed against
        // the alternatives like anything else. The action is unchanged — the work stays in the plan,
        // nothing will attempt it again, and whatever it was holding is released by whoever knows what
        // that means. Clearing HaltedAt is what tells the topology's routing to fall through to the
        // plan rather than reading this as stuck.
        if (chosen.Abandon is { } why)
        {
            await supervision.AbandonAsync(
                coordination.Result.Tree.PlanId,
                haltedAt,
                why,
                PlanCoordination.Answerer,
                ct).ConfigureAwait(false);

            return write(state, coordination with
            {
                Result = coordination.Result with { HaltedAt = null },
                Decision = null,
                Question = null
            });
        }

        // The plan is not changing: a choice inside the work has been made by somebody entitled to
        // make it. A step that did its task is marked done with the answer; any other step reads it
        // on its next attempt. No version, no re-ruling — and nothing left for the coordinator to
        // decide, the same as giving a step up.
        if (chosen.Answer is { } answer)
        {
            await supervision.AnswerAsync(
                coordination.Result.Tree.PlanId, haltedAt, answer, PlanCoordination.Answerer, ct)
                .ConfigureAwait(false);

            return write(state, coordination with
            {
                Result = coordination.Result with { HaltedAt = null },
                Decision = null,
                Question = null
            });
        }

        // Asking for a replan carries no contract of its own — the Planner writes it, from the
        // rationale and the rest of what halted — so nothing is minted here.
        if (chosen.Replan)
            return write(state, coordination with
            {
                Decision = PlanDecision.Replan(chosen.Rationale
                    ?? new PlanRationale { By = PlanRoles.Supervisor, Text = chosen.Summary }),
                Question = null
            });

        return write(state, coordination with { Decision = PlanDecision.Ask([]), Question = null });
    }
}
