using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A step waiting to be told something, and what an answer does about it.
/// </summary>
/// <remarks>
/// Asking is <see cref="PlanExecutor.AskAsync"/> now — the supervisor's own call (R31), never a step
/// reaching and reporting a choice on its own (that self-report path, and the guards that arbitrated
/// it, are gone under R26). What is left worth testing here is what asking leaves on the record, and
/// what answering it does — and does not — change.
/// </remarks>
[TestFixture]
public class BlockedOnChoiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task BeingBlocked_MintsNoVersionAndRewordsNoCriterion()
    {
        var store = await Seeded();
        var before = (await store.LoadAsync("trip"))!;
        var executor = new PlanExecutor(store);

        await executor.AskAsync(
            "trip", "hakone", "which window?", ["the 2nd to the 3rd", "the 6th to the 7th"]);
        var after = await executor.AnswerAsync("trip", "hakone", "the 6th to the 7th", "a person");

        after.Lineage.Count.ShouldBe(before.Lineage.Count);
        after.Node("hakone").Contract.ShouldBe(before.Node("hakone").Contract);
    }

    [Test]
    public async Task TheAnswer_ClearsTheQuestionAndReachesTheNextAttempt()
    {
        // The only channel there is: nothing is handed from one attempt to the next, so an answer
        // that is not read off the node is not read at all.
        var store = await Seeded();
        var executor = new PlanExecutor(store);

        await executor.AskAsync(
            "trip", "hakone", "which window?", ["the 2nd to the 3rd", "the 6th to the 7th"]);
        await executor.AnswerAsync("trip", "hakone", "the 6th to the 7th", "a person");

        string? told = null;

        await executor.ExecuteAsync("trip", (ctx, _) =>
        {
            if (ctx.Node.Id == "hakone")
                told = ctx.Node.Answers.SingleOrDefault()?.Answer;

            return Task.FromResult(Meets(ctx));
        });

        told.ShouldBe("the 6th to the 7th");
        (await store.LoadAsync("trip"))!.Node("hakone").Question.ShouldBeNull();
    }

    [Test]
    public async Task AnsweringSomethingThatIsNotWaiting_SaysSoRatherThanRecordingIt()
    {
        var store = await Seeded();

        await Should.ThrowAsync<InvalidOperationException>(
                new PlanExecutor(store).AnswerAsync("trip", "hakone", "whatever", "a person"))
            .ShouldNotBeNull();
    }

    [Test]
    public async Task BeingBlocked_IsReportedWithWhatWasAskedAndWhatWouldAnswerIt()
    {
        var store = await Seeded();
        var seen = new List<WorkflowEvent>();
        using var sink = WorkflowEventReporting.BeginScope(new Collecting(seen));

        var executor = new PlanExecutor(store);
        await executor.AskAsync(
            "trip", "hakone", "which window?", ["the 2nd to the 3rd", "the 6th to the 7th"]);
        await executor.AnswerAsync("trip", "hakone", "the 6th to the 7th", "a person");

        seen.OfType<PlanNodeBlocked>().ShouldHaveSingleItem().Options.Count.ShouldBe(2);
        seen.OfType<PlanNodeAnswered>().ShouldHaveSingleItem().Answer.ShouldBe("the 6th to the 7th");
    }

    [Test]
    public async Task AskAsync_MarksTheStepBlocked()
    {
        var store = await Seeded();
        var executor = new PlanExecutor(store);

        var asked = await executor.AskAsync(
            "trip", "hakone", "which window?", ["the 2nd to the 3rd", "the 6th to the 7th"]);

        asked.Node("hakone").State.ShouldBe(StepState.Blocked);
        asked.State.ShouldBe(StepState.Blocked);
    }

    // ── Fixtures ──

    private static async Task<IPlanTreeStore> Seeded()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree()).ConfigureAwait(false);
        return store;
    }

    private static PlanTree Tree() => PlanTree.Create(
        "trip",
        Contract("A trip", "within_duration()"),
        [("hakone", Contract("Nights in Hakone", "nights(hakone, 3)"))]);

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static NodeOutcome Meets(PlanNodeContext ctx) => new()
    {
        Verdicts =
        [
            .. ctx.Contract.AcceptanceCriteria.Select(c => new CriterionVerdict
            {
                Criterion = c, Passed = true, Oracle = "the itinerary", At = T0
            })
        ]
    };

    private sealed class Collecting(List<WorkflowEvent> into) : IWorkflowEventSink
    {
        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            into.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
