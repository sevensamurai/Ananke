using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The question no criterion can be asked: did satisfying all of them produce what was wanted?
/// </summary>
/// <remarks>
/// <para>
/// <b>Written against a run that passed everything and was wrong.</b> A supervisor re-ruled the
/// hardest step of a trip into *"go to the island and stay overnight — return planned in
/// follow-up"*, its own rationale admitting the dependency it left. Every criterion held, the plan
/// reported <em>Satisfied</em>, and the family had no way home.
/// </para>
/// <para>
/// <b>And no fixed set of criteria could have caught it</b>, because the criteria that mattered were
/// written by the supervisor as part of the change. The goal is the part it may not rewrite. That is
/// what the review reads.
/// </para>
/// </remarks>
[TestFixture]
public class PlanReviewTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    private sealed record Trip
    {
        public PlanCoordination? Coordination { get; init; }
    }

    [Test]
    public async Task AnAcceptedPlan_IsLeftExactlyAsItWas()
    {
        var before = Settled();

        var after = await Review((_, _) => Task.FromResult(PlanReview.Accept()), before);

        after.Coordination!.Result.HaltedAt.ShouldBeNull();
        after.Coordination.Result.Tree.Root.Violation.ShouldBeNull();
    }

    [Test]
    public async Task ARejection_BecomesAHaltAgainstTheRoot_SoItTravelsWhereHaltsTravel()
    {
        // Nothing is added to the loop. A rejected plan is a halted plan, so the supervisor is asked,
        // a person is shown the options where one is wired, and the round is charged as any other.
        var after = await Review(
            (_, _) => Task.FromResult(PlanReview.Reject("nothing brings the family home")),
            Settled());

        var coordination = after.Coordination!;

        coordination.Result.HaltedAt.ShouldBe("trip");
        coordination.Dispute!.Reason.ShouldBe("nothing brings the family home");

        // The halt reason a version would record is the reviewer's words, taken from the witness
        // exactly as any other dispute is.
        coordination.HaltReason.ShouldBe("nothing brings the family home");
    }

    [Test]
    public async Task ARejection_DisputesTheGoal_NotACriterion()
    {
        // The distinction the whole review rests on. Criteria were met; the goal was not.
        var after = await Review(
            (_, _) => Task.FromResult(PlanReview.Reject("the island is a one-way trip")),
            Settled());

        after.Coordination!.Dispute!.Criterion.ShouldBe("Plan a family trip they can come home from");
    }

    [Test]
    public async Task APlanThatHasNotFinished_IsNotReviewed()
    {
        // There is nothing to review: the work has not happened. Asking anyway would be reading a
        // half-run as though it were a result.
        var asked = false;

        await Review(
            (_, _) =>
            {
                asked = true;
                return Task.FromResult(PlanReview.Reject("no"));
            },
            Halted());

        asked.ShouldBeFalse();
    }

    [Test]
    public async Task AReviewerThatSaysNothing_IsTakenAsAcceptance()
    {
        // Silence is not a fault found. Stopping finished work on no evidence is worse than missing
        // one, and a reviewer that produced no answer has produced no evidence.
        var after = await Review((_, _) => Task.FromResult(PlanReview.Accept()), Settled());

        after.Coordination!.Result.HaltedAt.ShouldBeNull();
    }

    [Test]
    public async Task ARejection_IsRecordedInTheStore_NotOnlyInState()
    {
        // Asserting the in-state copy alone is exactly what hid F3: the tree that
        // `PlanExecutor.ExecuteAsync` reloads on the next pass is the store's, not workflow state's.
        var (_, store) = await ReviewWithStore(
            (_, _) => Task.FromResult(PlanReview.Reject("nothing brings the family home")),
            Settled());

        var saved = (await store.LoadAsync("trip")).ShouldNotBeNull();
        saved.Node("trip").Violation.ShouldNotBeNull().Reason.ShouldBe("nothing brings the family home");
    }

    // ── Fixtures ──

    private static async Task<Trip> Review(PlanReviewer reviewer, PlanCoordination coordination) =>
        (await ReviewWithStore(reviewer, coordination).ConfigureAwait(false)).State;

    private static async Task<(Trip State, InMemoryPlanTreeStore Store)> ReviewWithStore(
        PlanReviewer reviewer, PlanCoordination coordination, TimeProvider? clock = null)
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(coordination.Result.Tree).ConfigureAwait(false);

        var supervision = new SupervisionOptions
        {
            Store = store,
            TimeProvider = clock ?? TimeProvider.System
        };

        var job = new PlanReviewJob<Trip>(
            "review",
            reviewer,
            supervision,
            state => state.Coordination,
            (state, updated) => state with { Coordination = updated });

        var after = await job.ExecuteAsync(new Trip { Coordination = coordination }).ConfigureAwait(false);
        return (after, store);
    }

    private static PlanTree Tree() => PlanTree.Create(
        "trip",
        new AgentContract
        {
            Goal = "Plan a family trip they can come home from",
            AcceptanceCriteria = ["every_day_fits()"]
        },
        [("day-3", new AgentContract
        {
            Goal = "Go to the island and stay overnight",
            AcceptanceCriteria = ["fits(day-3)", "stays(day-3)"]
        })]);

    private static PlanCoordination Settled() => new()
    {
        Result = new PlanRunResult
        {
            Tree = Tree(),
            Executed = ["day-3"],
            Skipped = [],
            Rulings = new Dictionary<string, Verification>(),
            HaltedAt = null
        }
    };

    private static PlanCoordination Halted() => new()
    {
        Result = new PlanRunResult
        {
            Tree = Tree().WithViolation("day-3", new PlanViolation
            {
                Criterion = "fits(day-3)",
                Reason = "it does not fit",
                At = T0
            }),
            Executed = ["day-3"],
            Skipped = [],
            Rulings = new Dictionary<string, Verification>(),
            HaltedAt = "day-3"
        }
    };
}
