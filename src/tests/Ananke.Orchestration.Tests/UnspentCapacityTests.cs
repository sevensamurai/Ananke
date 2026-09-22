using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Work that achieves its goal and leaves something in the budget.
/// </summary>
/// <remarks>
/// <b>Not a rejection, and the distinction is the ruling.</b> A plan that works while leaving a third
/// of its budget unused is not <em>wrong</em>, it is <em>improvable</em> — and rejecting it to force
/// re-planning abuses the verdict exactly as encoding an ambiguity as a dispute does. So the verdict
/// stays binary and the finding travels beside it; what it produces is a halt that asks whether the
/// remainder is worth spending, which is a decision and not a fault.
/// </remarks>
[TestFixture]
public class UnspentCapacityTests
{
    [Test]
    public async Task AcceptedWithNothingLeft_EndsTheRun()
    {
        var after = await Reviewed(PlanReview.Accept());

        after.Coordination!.Result.HaltedAt.ShouldBeNull();
    }

    [Test]
    public async Task AcceptedWithSomethingLeft_AsksWhetherItIsWorthSpending()
    {
        var after = await Reviewed(PlanReview.Accept(
            new PlanUnspent { What = "3 of 12 nights are unused", CouldBuy = "a night in Nikko" }));

        var result = after.Coordination!.Result;

        result.HaltedAt.ShouldBe("trip");
        result.Tree.Node("trip").Violation.ShouldBeNull("nothing about the plan is disputed");
        result.Tree.Node("trip").Question.ShouldNotBeNull().Asks
            .ShouldContain("a night in Nikko");
    }

    [Test]
    public async Task AcceptedWithSomethingLeft_CarriesNoOptions_SoTheSupervisorIsAskedForThem()
    {
        // Unlike a step that reached a choice, nobody has found the alternatives yet — so this halt
        // goes down the ordinary propose path rather than the recommend-one-of-these path.
        var after = await Reviewed(PlanReview.Accept(new PlanUnspent { What = "3 nights spare" }));

        after.Coordination!.Result.Tree.Node("trip").Question.ShouldNotBeNull()
            .Options.ShouldBeEmpty();
    }

    [Test]
    public async Task Rejected_StillTravelsAsADisputeAgainstTheGoal()
    {
        // The other half of the distinction: a rejection says the work is not what was asked for.
        var after = await Reviewed(PlanReview.Reject("hakone was never booked"));

        var result = after.Coordination!.Result;

        result.Tree.Node("trip").Violation.ShouldNotBeNull().Reason.ShouldBe("hakone was never booked");
    }

    [Test]
    public async Task AnAcceptedReviewWithCapacityLeft_RecordsItsQuestionInTheStore()
    {
        // Asserting the in-state copy alone is exactly what hid F3: the tree that
        // `PlanExecutor.ExecuteAsync` reloads on the next pass is the store's, not workflow state's.
        var (_, store) = await ReviewedWithStore(PlanReview.Accept(
            new PlanUnspent { What = "3 of 12 nights are unused", CouldBuy = "a night in Nikko" }));

        var saved = (await store.LoadAsync("trip")).ShouldNotBeNull();
        saved.Node("trip").Question.ShouldNotBeNull().Asks.ShouldContain("a night in Nikko");
    }

    [Test]
    public async Task ARaisedReview_CanBeAnswered()
    {
        // The sharp end of F3: before the fix, the question lived only in workflow state, and
        // AnswerAsync — which loads from the store — threw "is not waiting to be told anything".
        var (_, store) = await ReviewedWithStore(
            PlanReview.Accept(new PlanUnspent { What = "3 of 12 nights are unused" }));

        var executor = new PlanExecutor(store);
        var answered = await executor.AnswerAsync("trip", "trip", "spend it on Nikko", "a person");

        answered.Node("trip").Answers.ShouldHaveSingleItem().Answer.ShouldBe("spend it on Nikko");
    }

    [Test]
    public async Task TheReviewsTimestamps_ComeFromTheSupervisionsClock()
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero));

        var (_, store) = await ReviewedWithStore(
            PlanReview.Accept(new PlanUnspent { What = "3 of 12 nights are unused" }), clock);

        var saved = (await store.LoadAsync("trip")).ShouldNotBeNull();
        saved.Node("trip").Question.ShouldNotBeNull().At.ShouldBe(clock.GetUtcNow());
    }

    // ── Fixtures ──

    private static async Task<Held> Reviewed(PlanReview verdict) =>
        (await ReviewedWithStore(verdict).ConfigureAwait(false)).State;

    private static async Task<(Held State, InMemoryPlanTreeStore Store)> ReviewedWithStore(
        PlanReview verdict, TimeProvider? clock = null)
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree()).ConfigureAwait(false);

        var supervision = new SupervisionOptions
        {
            Store = store,
            TimeProvider = clock ?? TimeProvider.System
        };

        var state = new Held
        {
            Coordination = new PlanCoordination
            {
                Result = new PlanRunResult
                {
                    Tree = Tree(),
                    Executed = ["trip"],
                    Skipped = [],
                    Rulings = new Dictionary<string, Verification>(StringComparer.Ordinal)
                }
            }
        };

        var job = new PlanReviewJob<Held>(
            "review",
            (_, _) => Task.FromResult(verdict),
            supervision,
            s => s.Coordination,
            (s, c) => s with { Coordination = c });

        var after = await job.ExecuteAsync(state).ConfigureAwait(false);
        return (after, store);
    }

    private static PlanTree Tree() => PlanTree.Create(
        "trip",
        new AgentContract { Goal = "Two weeks in Japan", AcceptanceCriteria = ["within_duration()"] },
        Array.Empty<(string, AgentContract)>());

    private sealed record Held
    {
        public PlanCoordination? Coordination { get; init; }
    }
}
