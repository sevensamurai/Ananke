using Ananke.Orchestration.Jobs;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// Reviews a settled plan against its goal, and turns a rejection into a halt.
/// </summary>
/// <remarks>
/// <para>
/// <b>A rejection is a halt, and travels the way halts travel.</b> It is recorded as a dispute
/// against the root — the step whose goal was not met — so the supervisor is asked what the plan
/// should become, a person is shown the options where one is wired, and the round is charged like
/// any other. Nothing new is added to the loop; the review is a place the run passes through on its
/// way out.
/// </para>
/// <para>
/// <b>It runs only when there is nothing left to run</b>, and it accepts silence. A reviewer that
/// answered nothing has not found a fault, and stopping finished work on no evidence is worse than
/// missing one.
/// </para>
/// <para>
/// <b>What bounds it is the change-of-plan budget.</b> Reject, re-rule, run, review again — that is
/// the cycle, and it is the same one a halt already takes, so it is already counted.
/// </para>
/// </remarks>
/// <param name="name">The job's name, as it appears in history and topology.</param>
/// <param name="review">Who reads the finished work.</param>
/// <param name="supervision">The supervision whose store and clock the review's halt is recorded through.</param>
/// <param name="read">Where the plan's progress is in state.</param>
/// <param name="write">How to put it back.</param>
public sealed class PlanReviewJob<TState>(
    string name,
    PlanReviewer review,
    SupervisionOptions supervision,
    Func<TState, PlanCoordination?> read,
    Func<TState, PlanCoordination, TState> write) : IJob<TState>
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        // Only a plan with nothing left to do is finished work. A halted one has not produced
        // anything to review, and reviewing it would be asking about work that has not happened.
        if (read(state) is not { } coordination || coordination.Result.HaltedAt is not null)
            return state;

        var verdict = await review(coordination, ct).ConfigureAwait(false);

        var tree = coordination.Result.Tree;
        var root = tree.Root.Id;

        // Accepted, with capacity left that somebody may want to spend. Not a rejection — the work
        // is what was asked for — so it does not travel as a dispute against the goal. It is a
        // decision that is available and is not the process's to take, which is what Blocked means,
        // and it carries no options because nobody has found any yet: the Supervisor is asked what
        // could be done with it, the way it is asked about any other halt.
        if (verdict is { Accepted: true, Unspent: { } unspent })
        {
            var asks = string.IsNullOrWhiteSpace(unspent.CouldBuy)
                ? $"{unspent.What} — is it worth spending?"
                : $"{unspent.What} — is it worth spending? It could buy {unspent.CouldBuy}.";

            var asking = await supervision.AskAsync(tree.PlanId, root, asks, ct: ct).ConfigureAwait(false);

            return write(state, coordination with
            {
                Result = coordination.Result with
                {
                    Tree = asking,
                    HaltedAt = root
                },
                Decision = null,
                Question = null
            });
        }

        if (verdict.Accepted)
            return state;

        var disputed = await supervision
            .DisputeAsync(tree.PlanId, root, tree.Root.Contract.Goal, verdict.Reason, ct)
            .ConfigureAwait(false);

        return write(state, coordination with
        {
            Result = coordination.Result with
            {
                Tree = disputed,
                HaltedAt = root
            },
            Decision = null,
            Question = null
        });
    }
}
