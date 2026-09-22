using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// S7b: an answer can settle something that outlives the halt it was given at.
/// </summary>
/// <remarks>
/// <para>
/// <b>Step 5 of the story is the whole story.</b> Asked what to do about a step that will not fit,
/// somebody answers in their own words — <em>keep Naoshima; find the slack somewhere else</em> — and
/// that is two things: an answer to this halt, and a preference about the plan. Consumed only as the
/// first, the preference dies with the halt, and two versions later the planner proposes dropping
/// Naoshima again. That is how a run makes a person say the same thing three times and looks stupid
/// doing it.
/// </para>
/// <para>
/// <b>So the assertion is deliberately not about the round it was said in.</b> Anything can honour a
/// preference in the round it hears it. What is being pinned is that it still binds after two further
/// changes of plan, authored by something that never saw the conversation.
/// </para>
/// <para>
/// <b>And both sentences survive, kept apart (R33).</b> Everything downstream binds the supervisor's
/// reading, which is exactly why the words it was read from have to stay next to it.
/// </para>
/// </remarks>
[TestFixture]
public class PlanTermTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private const string Said = "keep Naoshima, and find the slack somewhere else";
    private const string Reading = "Naoshima stays on the trip";

    [Test]
    public async Task ATermReadFromAnAnswer_StillBindsTwoVersionsLater()
    {
        // S7b's own assertion, and the reason the term lives on the plan rather than on the halt.
        var store = await Seeded();
        var executor = new PlanExecutor(store, new FakeClock());

        await executor.CommitAsync("trip", Reading, Said, "the traveller");

        // Two changes of plan after the one that heard it, neither of them told anything about it.
        await executor.ReruleAsync(
            "trip", "day-1", Contract("Plan day 1, shorter"), "it did not fit");

        await executor.ReruleAsync(
            "trip", "day-2", Contract("Plan day 2, shorter"), "it did not fit either");

        var tree = (await store.LoadAsync("trip")).ShouldNotBeNull();

        tree.Current.Number.ShouldBe(3);
        tree.Node("day-2").Contract.Constraints.ShouldContain(Reading);
    }

    [Test]
    public async Task ATerm_BindsWhatIsAuthoredAfterIt_AndNotWhatWasAuthoredBefore()
    {
        // Committing one re-words no criterion that already exists: a step already under way is not
        // retrospectively held to a term nobody had when it started.
        var store = await Seeded();
        var executor = new PlanExecutor(store, new FakeClock());

        var before = (await store.LoadAsync("trip")).ShouldNotBeNull();
        before.Node("day-1").Contract.Constraints.ShouldNotContain(Reading);

        await executor.CommitAsync("trip", Reading, Said, "the traveller");

        var committed = (await store.LoadAsync("trip")).ShouldNotBeNull();

        // No version minted, and no live contract rewritten.
        committed.Lineage.Count.ShouldBe(1);
        committed.Node("day-1").Contract.Constraints.ShouldNotContain(Reading);

        await executor.ReruleAsync("trip", "day-1", Contract("Plan day 1, shorter"), "it did not fit");

        (await store.LoadAsync("trip")).ShouldNotBeNull()
            .Node("day-1").Contract.Constraints.ShouldContain(Reading);
    }

    [Test]
    public async Task ATerm_KeepsThePersonsOwnWordsBesideTheReadingOfThem()
    {
        var store = await Seeded();

        await new PlanExecutor(store, new FakeClock())
            .CommitAsync("trip", Reading, Said, "the traveller");

        var term = (await store.LoadAsync("trip")).ShouldNotBeNull().Terms.ShouldHaveSingleItem();

        term.Reading.ShouldBe(Reading);
        term.Said.ShouldBe(Said);
        term.By.ShouldBe("the traveller");
        term.InForce.ShouldBeTrue();
    }

    [Test]
    public async Task AContradictedTerm_StopsBindingAndKeepsSayingWhyItStopped()
    {
        var store = await Seeded();
        var executor = new PlanExecutor(store, new FakeClock());

        var committed = await executor.CommitAsync("trip", Reading, Said, "the traveller");
        var id = committed.Terms.ShouldHaveSingleItem().Id;

        await executor.ContradictAsync(
            "trip", id, "the ferry stopped running altogether",
            PlanTermEnd.Retracted, "the traveller");
        await executor.ReruleAsync("trip", "day-1", Contract("Plan day 1, shorter"), "it did not fit");

        var tree = (await store.LoadAsync("trip")).ShouldNotBeNull();

        // Gone from what binds, and still on the record with the reason it went — revoked, not deleted.
        tree.Node("day-1").Contract.Constraints.ShouldNotContain(Reading);

        var term = tree.Terms.ShouldHaveSingleItem();
        term.InForce.ShouldBeFalse();
        term.Contradicted.ShouldBe("the ferry stopped running altogether");
        term.End.ShouldBe(PlanTermEnd.Retracted);
        term.EndedBy.ShouldBe("the traveller");
        term.Said.ShouldBe(Said);
    }

    [Test]
    public async Task ATerm_IsNotSaidTwiceWhenARerulingAlreadyCarriesIt()
    {
        // Constraints are rendered into every request a node makes, so a term that accumulated once
        // per change of plan would grow the prompt by one copy of itself per version.
        var store = await Seeded();
        var executor = new PlanExecutor(store, new FakeClock());

        await executor.CommitAsync("trip", Reading, Said, "the traveller");
        await executor.ReruleAsync("trip", "day-1", Contract("Plan day 1, shorter"), "one");
        await executor.ReruleAsync("trip", "day-1", Contract("Plan day 1, shorter still"), "two");

        (await store.LoadAsync("trip")).ShouldNotBeNull()
            .Node("day-1").Contract.Constraints.Count(c => c == Reading).ShouldBe(1);
    }

    [Test]
    public async Task ContradictingATermNobodyCommitted_SaysSoRatherThanDoingNothingQuietly()
    {
        var store = await Seeded();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new PlanExecutor(store, new FakeClock())
                .ContradictAsync("trip", "nope", "no reason", PlanTermEnd.Retracted, "somebody"));
    }

    // ── Fixtures ──

    private static async Task<InMemoryPlanTreeStore> Seeded()
    {
        var store = new InMemoryPlanTreeStore();

        await store.SaveAsync(PlanTree.Create(
            "trip",
            new AgentContract { Goal = "Plan a trip", AcceptanceCriteria = ["fits(trip)"] },
            [
                ("day-1", Contract("Plan day 1")),
                ("day-2", Contract("Plan day 2"))
            ])).ConfigureAwait(false);

        return store;
    }

    private static AgentContract Contract(string goal) =>
        new() { Goal = goal, AcceptanceCriteria = ["fits(day)"] };

    private sealed class FakeClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => T0;
    }
}
