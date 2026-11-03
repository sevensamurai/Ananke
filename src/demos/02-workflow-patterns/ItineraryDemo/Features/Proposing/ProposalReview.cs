using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Workflows;
using ItineraryDemo.Model;
using ItineraryDemo.Shared;

namespace ItineraryDemo.Features.Proposing;

/// <summary>The proposal as it stands, and what the last check found wrong with it.</summary>
internal sealed record Proposing
{
    public required string Itinerary { get; init; }

    public string Draft { get; init; } = "";

    public IReadOnlyList<string> Mismatches { get; init; } = [];

    public bool Approved { get; init; }

    public int Rounds { get; init; }
}

/// <summary>What the drafter wrote.</summary>
internal sealed record ProposalDraft
{
    public required string Text { get; init; }
}

/// <summary>What the check found.</summary>
internal sealed record ProposalCheck
{
    public required bool Approved { get; init; }

    public IReadOnlyList<string> Mismatches { get; init; } = [];
}

/// <summary>How the review ended, and what it cost.</summary>
internal sealed record ProposalOutcome(
    string Draft, bool Approved, int Rounds, IReadOnlyList<string> Mismatches, bool OverBudget, decimal Spent);

/// <summary>
/// Writes what the traveller is told, and checks it against the itinerary before it is handed over.
/// </summary>
/// <remarks>
/// <para>
/// <b>One model drafts, another checks.</b> The drafter writes the proposal from the recommended
/// stays; the checker reads it against the same stays and names every place, date or stay that does
/// not match. The draft is rewritten until nothing does, or the rounds run out — and the ending says
/// which, because a proposal that was never checked clean must not read as one that was.
/// </para>
/// <para>
/// <b>The same budget.</b> It runs only after a plan that completed, on whatever the plan left of the
/// run's budget, with the same priced models.
/// </para>
/// </remarks>
internal static class ProposalReview
{
    /// <summary>How many drafts the checker reads before the review stops.</summary>
    public const int RoundsAllowed = 3;

    /// <summary>The review's workflow name, which tags what its model calls publish.</summary>
    public const string WorkflowName = "proposal-review";

    public static async Task<ProposalOutcome> RunAsync(
        TripModels models, IReadOnlyList<PlannedStay> stays, decimal remaining, CancellationToken ct = default)
    {
        var drafter = AgentJobFactory.Create<Proposing, ProposalDraft>("draft-proposal", models.Executor)
            .WithSystemPrompt(
                "You write a traveller's trip proposal: a short, warm summary of where they stay each "
                + "night. Name every place, date and stay exactly as the itinerary gives it, and add none.")
            .WithPrompt(state => $"""
                The itinerary:
                {state.Itinerary}
                {Corrections(state)}
                Write the proposal.
                """)
            .MapResult((state, draft) => state with { Draft = draft.Text, Rounds = state.Rounds + 1 })
            .Build();

        var checker = AgentJobFactory.Create<Proposing, ProposalCheck>("check-proposal", models.Supervisor)
            .WithSystemPrompt(
                "You check a trip proposal against its itinerary. For every place, date and stay the "
                + "proposal names, say whether the itinerary has it. List each one that does not match, "
                + "quoting the proposal. Approve only when nothing in it disagrees with the itinerary.")
            .WithPrompt(state => $"""
                The itinerary:
                {state.Itinerary}

                The proposal:
                {state.Draft}
                """)
            .MapResult((state, check) => state with
            {
                Approved = check.Approved && check.Mismatches.Count == 0,
                Mismatches = check.Mismatches
            })
            .Build();

        var review = AgenticPattern.ReviewCritique<Proposing>(WorkflowName)
            .WithGenerator(drafter)
            .WithCritic(checker)
            .Until(state => state.Approved)
            .MaxIterations(RoundsAllowed)
            .Build()
            .WithBudget(new BudgetConfig { MaxCost = remaining });

        var execution = await review.RunAsync(new Proposing { Itinerary = Lines(stays) }, ct).ConfigureAwait(false);
        var state = execution.State;

        return new ProposalOutcome(
            state.Draft,
            state.Approved,
            state.Rounds,
            state.Mismatches,
            execution.Status is ExecutionStatus.BudgetExceeded,
            execution.EstimatedCost);
    }

    /// <summary>The recommended stays, one line each, as both models read them.</summary>
    public static string Lines(IReadOnlyList<PlannedStay> stays) =>
        string.Join(Environment.NewLine, stays.Select(stay =>
            $"- {stay.CheckIn:ddd d MMM} to {stay.CheckOut:ddd d MMM}: {stay.Place}, {stay.Choice}"));

    private static string Corrections(Proposing state) =>
        state.Mismatches.Count == 0
            ? ""
            : $"""

              The last draft got these wrong; correct them:
              {string.Join(Environment.NewLine, state.Mismatches.Select(m => $"- {m}"))}

              """;
}
