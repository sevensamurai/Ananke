using System.Text.Json;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Polymorphic JSON round-trip: catches a forgotten <c>[JsonDerivedType]</c> registration.
/// </summary>
/// <remarks>
/// <b>Not hypothetical.</b> <c>InMemoryCheckpointStore.SaveAsync</c> already calls
/// <c>JsonSerializer.Serialize</c> on every checkpoint, and workflow state carries a
/// <see cref="PlanCoordination"/> whenever a supervised plan is in it. Before <c>PlanDecision</c> was
/// registered as a polymorphic hierarchy, a coordination checkpointed with a decision in it lost the
/// decision silently on save and threw on load — a live defect, not a future one in a file store.
/// </remarks>
[TestFixture]
public class PlanCheckpointRoundTripTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    [TestCaseSource(nameof(DecisionShapes))]
    public void ACoordination_CarryingEachDecisionShape_RoundTripsThroughJson(PlanDecision decision)
    {
        var coordination = Halted() with { Decision = decision };

        var json = JsonSerializer.Serialize(coordination);
        var roundTripped = JsonSerializer.Deserialize<PlanCoordination>(json).ShouldNotBeNull();

        switch (decision, roundTripped.Decision)
        {
            case (PlanDecision.ReplanPlan expected, PlanDecision.ReplanPlan actual):
                actual.Contract!.Goal.ShouldBe(expected.Contract!.Goal);
                actual.Contract.AcceptanceCriteria.ShouldBe(expected.Contract.AcceptanceCriteria);
                actual.NodeId.ShouldBe(expected.NodeId);
                actual.Rationale.ShouldNotBeNull().By.ShouldBe(expected.Rationale!.By);
                break;

            case (PlanDecision.AskPlan expected, PlanDecision.AskPlan actual):
                actual.Options.Select(o => o.Summary).ShouldBe(expected.Options.Select(o => o.Summary));
                break;

            default:
                Assert.Fail(
                    $"'{decision.GetType().Name}' round-tripped as "
                    + $"'{roundTripped.Decision?.GetType().Name ?? "null"}'.");
                break;
        }
    }

    [Test]
    public void AReplanPlan_CarryingReplacementChildren_RoundTripsThemToo()
    {
        // Found by this suite before it was fixed: Children travelled as an
        // IReadOnlyList<ValueTuple>, and System.Text.Json ignores a ValueTuple's fields — so this
        // came back as [{},{}], every id and contract lost.
        var decision = PlanDecision.Replan(
            Contract("Ship it, streaming"),
            children:
            [
                new AuthoredStep { Id = "parse-strict", Contract = Contract("Parse, strictly") },
                new AuthoredStep { Id = "parse-legacy", Contract = Contract("Parse, leniently") }
            ]);

        var coordination = Halted() with { Decision = decision };

        var json = JsonSerializer.Serialize(coordination);
        var roundTripped = JsonSerializer.Deserialize<PlanCoordination>(json).ShouldNotBeNull();

        var replan = roundTripped.Decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
        var children = replan.Children.ShouldNotBeNull();
        children.Count.ShouldBe(2);
        children[0].Id.ShouldBe("parse-strict");
        children[0].Contract.Goal.ShouldBe("Parse, strictly");
        children[1].Id.ShouldBe("parse-legacy");
        children[1].Contract.Goal.ShouldBe("Parse, leniently");
    }

    [Test]
    public void ACoordination_CarryingAnOutstandingQuestion_RoundTripsThroughJson()
    {
        var coordination = Halted() with
        {
            Question = new PlanQuestion
            {
                Options =
                [
                    new PlanOption { Summary = "extend the deadline", Replan = true },
                    new PlanOption { Summary = "cut the feature", Answer = "cut it" }
                ],
                Picked = 2
            }
        };

        var json = JsonSerializer.Serialize(coordination);
        var roundTripped = JsonSerializer.Deserialize<PlanCoordination>(json).ShouldNotBeNull();

        var question = roundTripped.Question.ShouldNotBeNull();
        question.Options.Count.ShouldBe(2);
        question.Options[0].Summary.ShouldBe("extend the deadline");
        question.Options[0].Replan.ShouldBeTrue();
        question.Options[1].Answer.ShouldBe("cut it");
        question.Picked.ShouldBe(2);
    }

    [Test]
    public async Task ACheckpointedRun_PausedWithADecisionInState_Resumes()
    {
        // The test that would have caught it: InMemoryCheckpointStore.SaveAsync already serializes
        // every checkpoint, and a run paused with a decision already written to state — the shape a
        // person resuming an escalated round leaves behind — used to lose that decision on save and
        // throw NotSupportedException on load.
        var store = new InMemoryCheckpointStore();

        var checkpoint = new Checkpoint<Held>
        {
            ExecutionId = "trip-run",
            WorkflowName = "tokyo-trip",
            CurrentJob = "choose",
            State = new Held { Coordination = Halted() with { Decision = PlanDecision.Ask([]) } },
            Status = ExecutionStatus.Interrupted,
            History = [],
            CreatedAt = T0
        };

        await store.SaveAsync(checkpoint).ConfigureAwait(false);
        var resumed = await store.LoadAsync<Held>("trip-run").ConfigureAwait(false);

        resumed.ShouldNotBeNull().State.Coordination.ShouldNotBeNull().Decision
            .ShouldBeOfType<PlanDecision.AskPlan>();
    }

    // ── Fixtures ──

    private static IEnumerable<PlanDecision> DecisionShapes()
    {
        yield return PlanDecision.Replan(
            Contract("Parse, differently", "the parser round-trips"),
            rationale: new PlanRationale { By = "the runbook", Text = "the format needs two parsers" },
            nodeId: "root");
        yield return PlanDecision.Ask(
        [
            new PlanOption { Summary = "wait for more capacity", Recommended = true }
        ]);
    }

    private static PlanCoordination Halted() => new()
    {
        Result = new PlanRunResult
        {
            Tree = Tree(),
            Executed = ["parse"],
            Skipped = [],
            Rulings = new Dictionary<string, Verification>(),
            HaltedAt = "parse"
        },
        MaxChanges = 3
    };

    private static PlanTree Tree() => PlanTree.Create(
        "plan", Contract("Ship it"), [("parse", Contract("Parse", "the parser round-trips"))]);

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria.Length == 0 ? ["it is done"] : criteria };

    private sealed record Held
    {
        public PlanCoordination? Coordination { get; init; }
    }
}
