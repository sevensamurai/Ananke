using Ananke.Orchestration.Agents;

namespace Ananke.Orchestration.Planning;

/// <summary>What a reviewer made of finished work.</summary>
public sealed record PlanReview
{
    /// <summary>Whether the work is what the plan was for.</summary>
    public required bool Accepted { get; init; }

    /// <summary>Why not, when it is not. Empty when it is.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Capacity the work was granted and did not use, when there is any worth saying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Beside the verdict, not a third value of it.</b> A plan that works while leaving a third of
    /// its budget unused is not <em>wrong</em>, it is <em>improvable</em> — and rejecting it to force
    /// re-planning abuses the verdict exactly as encoding an ambiguity as a dispute does. So the
    /// verdict stays binary and this travels with it, the way a rationale travels beside a reason.
    /// </para>
    /// <para>
    /// <b>It is a finding, not an instruction.</b> Whether spare capacity is worth spending is
    /// somebody's judgement and not the reviewer's; what the reviewer owes is to say it is there,
    /// because a run that finishes under budget and reports nothing looks exactly like one that used
    /// everything it had.
    /// </para>
    /// </remarks>
    public PlanUnspent? Unspent { get; init; }

    /// <summary>Accepted, with nothing to say.</summary>
    public static PlanReview Accept() => new() { Accepted = true };

    /// <summary>Accepted, and there is capacity left that somebody may want to spend.</summary>
    public static PlanReview Accept(PlanUnspent unspent) =>
        new() { Accepted = true, Unspent = unspent };

    /// <summary>Rejected, for a stated reason.</summary>
    public static PlanReview Reject(string reason) => new() { Accepted = false, Reason = reason };
}

/// <summary>
/// Rules on finished work against the <b>goal</b>, once, when there is nothing left to run.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is not re-checking the steps.</b> The checks already ruled on every criterion, cheaply and
/// repeatably, as each step finished. Asking a model to do that again is a second opinion about
/// evidence — double work that adds no evidence, and replaces something that ran with something that
/// read.
/// </para>
/// <para>
/// <b>It answers the one question no criterion can: did satisfying all of them produce what was
/// wanted?</b> That gap is not hypothetical and it is not rare. A supervisor <em>authors the criteria
/// of its own replacement</em>, so a plan that changes mid-run is a plan whose later criteria were
/// written by the thing being checked. The goal is the part it may not rewrite, and this is what
/// reads it.
/// </para>
/// <para>
/// <b>Two conditions, or it is theatre.</b> It must be able to query the facts rather than only read
/// the record — a reviewer holding the transcript reads the worker's own account back and agrees
/// with it. And a rejection must be a halt like any other: it goes wherever halts go, and it spends
/// from the change-of-plan budget, so nothing can reject indefinitely.
/// </para>
/// </remarks>
public delegate Task<PlanReview> PlanReviewer(PlanCoordination coordination, CancellationToken ct);

/// <summary>Capacity the work was granted and did not use.</summary>
public sealed record PlanUnspent
{
    /// <summary>What is left, in the words of whoever noticed it.</summary>
    public required string What { get; init; }

    /// <summary>What it could buy, when that is obvious. <see langword="null"/> when it is not.</summary>
    public string? CouldBuy { get; init; }
}

/// <summary>What the model is asked for: whether the finished work is what the plan was for.</summary>
internal sealed record ReviewedWork
{
    /// <summary>Whether it is.</summary>
    public bool Accepted { get; init; }

    /// <summary>What is missing or wrong, if anything is.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>What the work was granted and did not use, if anything worth saying.</summary>
    public string Unspent { get; init; } = string.Empty;

    /// <summary>What that could buy, if it is obvious.</summary>
    public string CouldBuy { get; init; } = string.Empty;
}

/// <summary>
/// A reviewer that asks a model whether the finished plan is what its goal asked for.
/// </summary>
/// <remarks>
/// <b>It runs on the <see cref="PlanRoles.Supervisor"/> model and gets its tools</b>, for the same
/// reason the advisor does: a reviewer that cannot check anything can only agree with what it is
/// shown, and what it is shown was written by the work.
/// </remarks>
public sealed class AgentPlanReviewer(SupervisionOptions supervision, string? persona = null)
{
    private const string DefaultPersona =
        "You are reading finished work against the goal it was for. Every acceptance criterion has "
        + "already been checked by a program and every one of them held — do not re-check them, and "
        + "do not reject because you would have done it differently.\n\n"
        + "Ask one question: does what was actually produced achieve the goal? Look for what the "
        + "criteria did not cover — something the goal implies that nobody wrote down, a dependency "
        + "left unfinished, work that satisfies the letter of each step and not the point of the "
        + "whole. If you find nothing of that kind, accept.\n\n"
        + "Separately, and whatever you decide: if the work was granted something it did not use — "
        + "time, budget, room — say what is left and, if it is obvious, what it could buy. That is "
        + "not a reason to reject. Work that achieves its goal under budget is finished work, and "
        + "saying so is a fact somebody may want to act on rather than a fault you have found.";

    /// <summary>Reads the finished plan and says whether it is what was wanted.</summary>
    public async Task<PlanReview> ReviewAsync(
        PlanCoordination coordination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(coordination);

        var model = supervision.ModelFor(PlanRoles.Supervisor)
            ?? throw new InvalidOperationException(
                "An AgentPlanReviewer needs a model for the 'supervisor' role: set Supervisor on the "
                + "supervision, or add it to Models.");

        ReviewedWork? reviewed = null;

        var builder = AgentJobFactory.Create<PlanCoordination, ReviewedWork>("plan-review", model)
            .WithSystemPrompt(persona ?? DefaultPersona)
            .WithPrompt(Describe)
            .MapResult((state, produced) =>
            {
                reviewed = produced;
                return state;
            });

        if (supervision.SupervisorTools is { } tools)
            builder = builder.WithTools(tools).WithMaxToolRounds(supervision.SupervisorToolRounds ?? 12);

        try
        {
            await builder.Build().ExecuteAsync(coordination, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException exhausted) when (AgentPlanAdvisor.OutOfRounds(exhausted))
        {
            // A reviewer that ran out of rounds has not found a reason to reject; it has run out of
            // looking. Rejecting on that would stop work for the framework's reasons.
            return PlanReview.Accept();
        }

        // Nothing said is acceptance. A reviewer that produced no answer has not found a fault, and
        // treating silence as rejection would stop finished work on no evidence at all.
        if (reviewed is { Accepted: false } && !string.IsNullOrWhiteSpace(reviewed.Reason))
            return PlanReview.Reject(reviewed.Reason);

        // Accepted, and possibly with something left over. A finding rather than a fault: it does
        // not change the verdict, and a run that finishes under budget saying nothing looks exactly
        // like one that used everything it had.
        return reviewed is { } answer && !string.IsNullOrWhiteSpace(answer.Unspent)
            ? PlanReview.Accept(new PlanUnspent
            {
                What = answer.Unspent,
                CouldBuy = string.IsNullOrWhiteSpace(answer.CouldBuy) ? null : answer.CouldBuy
            })
            : PlanReview.Accept();
    }

    /// <summary>This reviewer as the delegate a workflow takes.</summary>
    public PlanReviewer AsReviewer() => ReviewAsync;

    private string Describe(PlanCoordination coordination)
    {
        var tree = coordination.Result.Tree;
        var projection = PlanTreeProjection.Project(
            tree, tree.Root.Id, supervision.ProjectionTokenBudget);

        var changes = tree.Lineage.Count <= 1
            ? "The plan ran as it was written; nothing changed it."
            : $"""
               The plan changed {tree.Lineage.Count - 1} time(s) while it ran. What each change was
               for:
               {string.Join('\n', tree.Lineage.Skip(1).Select(
                   v => $"  v{v.Number}, re-ruling '{v.ReRuledNodeId}': {v.Reason}"))}

               A change of plan rewrites the criteria of the step it replaces. Those criteria were
               authored by the same role that is now reading them back, so they are the part of this
               record least able to tell you it fell short.
               """;

        return $"""
            The goal the whole plan was for:
            {tree.Root.Contract.Goal}

            What its own criteria required: {string.Join(" | ", tree.Root.Contract.AcceptanceCriteria)}

            The finished plan:
            {projection.Text}

            {changes}

            Every acceptance criterion was checked and held. Say whether the work achieves the goal.
            If it does not, say what is missing in one sentence — the thing that was needed and that
            no criterion asked for.
            """;
    }
}
