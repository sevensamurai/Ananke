using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tracing;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// Wraps a consumer's own coordinator job so the run's accounting stays with the framework.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The coordinator seat takes two forms — a <see cref="PlanSupervisor"/>
/// delegate, and an <see cref="IJob{TState}"/> for a decider that is a person, a queue or a
/// sub-workflow. <see cref="PlanSupervisorJob{TState}"/> discharges four duties for the delegate
/// form; a job supplied in the same slot used to inherit none of them and be told about none, so the
/// form a consumer reaches for when the decider is *not* a model was the form with no contract.
/// </para>
/// <para>
/// <b>Two of the four move here, and one cannot.</b>
/// </para>
/// <list type="table">
///   <item><term>Decide</term><description>the job's, necessarily.</description></item>
///   <item><term>Apply a re-ruling</term><description><b>the job's.</b> It knows what it decided
///   before anyone else can, and re-ruling through the supervision it was handed is a single call.
///   Doing it here would mean guessing at the decision before the job has made it.</description></item>
///   <item><term>Spend the budget</term><description><b>here.</b> It is a pure function of what the
///   budget was and what was decided — and a job that forgot left the change-of-plan cap
///   unenforced, with the loop's iteration backstop quietly standing in for it.</description></item>
///   <item><term>Report the decision</term><description><b>here.</b> A job that forgot made the
///   tier's central act invisible to narration, tracing and every event consumer.</description></item>
/// </list>
/// <para>
/// <b>The one guarantee the job form cannot have.</b> For the delegate form the decision is reported
/// <em>before</em> it is applied, so an account reads in the order things happened. A job applies its
/// own decision, so by the time anything outside it can know what was decided, the version it minted
/// has already been reported. The event is therefore accurate and <b>late</b>: it follows the
/// <see cref="PlanVersionMinted"/> it explains rather than preceding it. Reporting early would mean
/// announcing a decision nobody had taken yet, which is worse than reporting it out of order.
/// </para>
/// <para>
/// <b>A job that decides nothing is not made to look decisive.</b> If no decision is written where
/// <c>Tracking</c> reads it, nothing is reported and no budget is spent.
/// </para>
/// <para>
/// <b>Reporting is not accounting, and an escalated round wants only the first.</b> When the run was
/// paused before this job and somebody resumed it, the decision is still reported — a person's
/// judgement belongs in the record more than a model's does — and nothing is charged for it. That is
/// read from how the job was entered, never declared: the budget bounds the re-planning this loop
/// does by itself, and a round it paused for is bounded by somebody being there to answer.
/// </para>
/// <para>
/// <b>Public because the builder is not the only way to wire a plan.</b> <c>AgenticPattern.SupervisedPlan</c>
/// applies this for you; a workflow hand-wired from <c>Supervise</c>, <c>Job</c> and <c>Loop</c> — the
/// shape you need as soon as a coordinator has to pause <em>between</em> proposing and deciding — has
/// to wrap its own coordinator job to get the same accounting. Wrap it once, at registration:
/// <code>
/// .Job("choose", new AccountedSupervisorJob&lt;TState&gt;(
///     new Choose(supervision), s =&gt; s.Coordination, (s, c) =&gt; s with { Coordination = c }))
/// </code>
/// </para>
/// </remarks>
public sealed class AccountedSupervisorJob<TState>(
    IJob<TState> inner,
    Func<TState, PlanCoordination?> read,
    Func<TState, PlanCoordination, TState> write) : IJob<TState>
{
    /// <inheritdoc />
    /// <remarks>The inner job's name, because it is the handle: history, topology and
    /// <c>InterruptBefore</c> all name the job the consumer supplied, not a wrapper around it.</remarks>
    public string Name { get; } = inner.Name;

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        var before = read(state);
        var escalated = WorkflowTraceContext.Value is { ResumedInto: true };

        // A settled plan has nothing to decide, and nothing to account for. The job still runs — it
        // is on the main line, and the loop's predicate reads what it returns.
        if (before is null || before.Result.HaltedAt is not { } haltedAt)
            return await inner.ExecuteAsync(state, ct).ConfigureAwait(false);

        var after = await inner.ExecuteAsync(state, ct).ConfigureAwait(false);

        if (read(after) is not { Decision: { } decision } coordination)
            return after;

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanDecisionTaken
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = coordination.Result.Tree.PlanId,
            PlanVersion = coordination.Result.Tree.Current.Number,
            NodeId = haltedAt,
            Decision = decision,
            // The budget as it stood when the job was asked, not after it answered — the same two
            // numbers the delegate form reports, so an audit reads the same either way.
            Changes = before.Changes,
            MaxChanges = before.MaxChanges,
            Escalated = escalated
        }, ct).ConfigureAwait(false);

        // Computed from what the budget was, never from what the job wrote back. A job that spent it
        // by hand agrees with this; one that forgot is corrected; one that wrote something else does
        // not get to set the run's own cap — and an escalated round is held at what it was, including
        // against a job that charged itself.
        return write(after, coordination with
        {
            // Giving a step up is nobody's re-planning: no version is minted and no contract is
            // re-authored, so there is nothing for the change-of-plan budget to be counting.
            Changes = before.Changes + (PlanDecision.Spends(decision, escalated) ? 1 : 0)
        });
    }
}
