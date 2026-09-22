using Ananke.Orchestration.Planning;

namespace Ananke.Design;

/// <summary>
/// The three endings only the domain can word.
/// </summary>
/// <remarks>
/// A run that met its root, one somebody gave up and one that stopped before anything was planned are
/// about the work, not about the loop: "every night has a stay" and "the release is cut" are the same
/// ending in two domains. Everything else — which version was written and never ran, which step stopped
/// it, whether anything was offered — is a fact about the run and is worded here.
/// </remarks>
public sealed record PlanEndingWords
{
    /// <summary>Every contract in the plan is met.</summary>
    public string Done { get; init; } = "Done: the plan is met.";

    /// <summary>Somebody gave the work up rather than leave it unfinished.</summary>
    public string GivenUp { get; init; } = "Given up.";

    /// <summary>The run stopped with nothing halted and nothing met.</summary>
    public string NothingStarted { get; init; } = "Stopped with no step halted.";
}

/// <summary>
/// How a run ended, in one sentence.
/// </summary>
/// <remarks>
/// <b>The tier's own words are precise and unreadable.</b> <c>Blocked</c>, <c>Unmet</c> and "halted"
/// say nothing to somebody who has not read the design, so every consumer writes this switch over
/// <see cref="PlanRunOutcome"/>, the change-of-plan budget and the decision. It is written once here,
/// and what the domain calls its endings arrives in <see cref="PlanEndingWords"/>.
/// </remarks>
public static class PlanRunEnding
{
    /// <summary>The sentence for how <paramref name="coordination"/>'s run ended.</summary>
    /// <param name="coordination">Where the plan stands, after the last pass.</param>
    /// <param name="words">What this domain calls its three endings. Plain ones when omitted.</param>
    public static string Describe(PlanCoordination coordination, PlanEndingWords? words = null)
    {
        ArgumentNullException.ThrowIfNull(coordination);

        var said = words ?? new PlanEndingWords();
        var haltedAt = coordination.Result.HaltedAt;

        return coordination.Result.Outcome switch
        {
            PlanRunOutcome.Completed => said.Done,
            PlanRunOutcome.Abandoned => said.GivenUp,
            PlanRunOutcome.Faulted => $"Something broke while planning '{haltedAt}'.",

            // The change of plan that reaches the ceiling is still written, and then nothing runs it.
            _ when haltedAt is null && coordination.MaxChanges is { } ceiling && coordination.Changes >= ceiling =>
                $"Stopped: the plan changed {coordination.Changes} time(s), the most this run allows. "
                + $"Version {coordination.Result.Tree.Current.Number} was written and never ran.",

            _ when haltedAt is null => said.NothingStarted,

            _ when coordination.Decision is PlanDecision.AskPlan =>
                $"Stopped at '{haltedAt}' — nothing was offered that the run could take.",

            _ => $"Stopped at '{haltedAt}' — the Planner was asked and wrote nothing the run could use."
        };
    }
}
