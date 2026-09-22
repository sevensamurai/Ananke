using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// S9: a provider falling over is infrastructure until it is exhausted, and then it is evidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>A transient is never a kind of halt.</b> The loop re-issues the attempt, mechanically, and the
/// plan is not told: no attempt count reaches the tree, so nothing downstream can start reasoning
/// about how many goes a step has had. What survives is the finding at the end — <em>could not be
/// run: 3 attempts, …</em> — which is prose from the world, like any other finding.
/// </para>
/// <para>
/// <b>And a depleted account is not a transient.</b> Both arrive as the same status code, so the
/// distinction is the provider's own words and is taken once, where the exception still exists.
/// Retrying into a spend cap spends calls that were never going to succeed against a limit somebody
/// set on purpose — and then, worse, escalates the halt into a supervisor on the same credentials and
/// reports the resulting silence as <em>nobody could propose anything</em>, which is a statement about
/// the plan rather than about the account.
/// </para>
/// </remarks>
[TestFixture]
public class TransientRetryTests
{
    private const string Depleted =
        "You exceeded your current quota, please check your plan and billing details.";

    // ── S9 — the loop retries, and the plan never learns how often ──

    [Test]
    public async Task ATransientFailure_IsReIssuedUntilItWorks_AndTheTreeIsNeverToldItHappened()
    {
        var attempts = 0;

        // Counted for the step alone: once it recovers the pass carries on to the root, which runs
        // too, and a counter spanning both would be measuring the walk rather than the retry.
        var result = await Running((ctx, _) =>
            ctx.Node.Id == "step" && ++attempts < 3
                ? throw new HttpRequestException("503 Service Unavailable")
                : Task.FromResult(new NodeOutcome { Summary = "done" }));

        attempts.ShouldBe(3);

        // It recovered, so there is no failure on the record at all — and nothing anywhere counts.
        result.HaltedAt.ShouldBeNull();
        result.Tree.Node("step").Failure.ShouldBeNull();
    }

    [Test]
    public async Task ATransientThatNeverClears_BecomesAnOrdinaryFinding_NamingWhatWasTried()
    {
        var attempts = 0;

        var result = await Running((_, _) =>
        {
            attempts++;
            throw new HttpRequestException("503 Service Unavailable");
        });

        attempts.ShouldBe(3);

        var failure = result.Tree.Node("step").Failure.ShouldNotBeNull();

        // The count lives in the sentence and nowhere a plan can read it — which is the whole of S9.
        failure.Message.ShouldContain("3 attempts");
        failure.Message.ShouldContain("503 Service Unavailable");
        failure.Terminal.ShouldBeFalse();
        result.HaltedAt.ShouldBe("step");
    }

    [Test]
    public void NothingOnTheTree_RecordsHowManyAttemptsAStepHasHad()
    {
        // Stated as a test because it is the property that would rot quietly: an `Attempts` field
        // added here for a dashboard is a field a planner can read, and then infrastructure starts
        // steering decisions it has no business steering.
        typeof(NodeFailure).GetProperties().Select(p => p.Name)
            .ShouldBe(["Message", "At", "Terminal"], ignoreOrder: true);
    }

    [Test]
    public async Task APolicyOfOne_DoesNotRetryAtAll()
    {
        var attempts = 0;

        await Running((_, _) =>
        {
            attempts++;
            throw new HttpRequestException("503 Service Unavailable");
        }, PlanRetryPolicy.Once);

        attempts.ShouldBe(1);
    }

    // ── A spend cap is the error class where retrying is actively harmful ──

    [Test]
    public async Task ADepletedAccount_IsNeverRetried_HoweverManyAttemptsThePolicyAllows()
    {
        var attempts = 0;

        var result = await Running((_, _) =>
        {
            attempts++;
            throw new HttpRequestException(Depleted);
        });

        // Once. Waiting cannot fix an account out of allowance, and re-issuing into one spends calls
        // against a limit somebody set deliberately.
        attempts.ShouldBe(1);

        var failure = result.Tree.Node("step").Failure.ShouldNotBeNull();
        failure.Terminal.ShouldBeTrue();
        failure.Message.ShouldContain("waiting will not fix it");
    }

    [Test]
    public async Task ADepletedAccount_SaysSoOnTheStream_RatherThanLookingLikeABusyOne()
    {
        var seen = new List<PlanNodeFailed>();

        using (WorkflowEventReporting.BeginScope(new Sink(seen)))
        {
            await Running((_, _) => throw new HttpRequestException(Depleted));
            await Running((_, _) => throw new HttpRequestException("503 Service Unavailable"));
        }

        seen.Count.ShouldBe(2);
        seen[0].Terminal.ShouldBeTrue();
        seen[1].Terminal.ShouldBeFalse();
    }

    [Test]
    public async Task AHaltNothingCanBeAskedAbout_IsNotEscalatedIntoMoreCallsThatWillFailTheSameWay()
    {
        // The expensive half. Every seat past the halt runs on the same credentials as the one that
        // just died, so asking is not evidence-gathering — it is buying the same failure again and
        // then reporting the silence as though the plan were the problem.
        var asked = 0;
        var result = await Running((_, _) => throw new HttpRequestException(Depleted));

        var coordination = new PlanCoordination { Result = result };
        coordination.Unanswerable.ShouldBeTrue();

        var job = new PlanAskingJob<Held>(
            "propose",
            (_, _) =>
            {
                asked++;
                return Task.FromResult(new PlanProposal { Options = [] });
            },
            new SupervisionOptions(),
            state => state.Coordination,
            (state, c) => state with { Coordination = c });

        var after = await job.ExecuteAsync(new Held { Coordination = coordination });

        asked.ShouldBe(0);
        after.Coordination!.Question.ShouldBeNull();
    }

    [Test]
    public async Task AnOrdinaryFailure_IsStillAskedAbout_BecauseSomebodyMightHaveAnAnswer()
    {
        // The control for the test above: the short-circuit must be about the account being dead,
        // not about failure in general. A provider that fell over is still a halt worth asking about.
        var asked = 0;
        var result = await Running((_, _) => throw new HttpRequestException("503 Service Unavailable"));

        new PlanCoordination { Result = result }.Unanswerable.ShouldBeFalse();

        var job = new PlanAskingJob<Held>(
            "propose",
            (_, _) =>
            {
                asked++;
                return Task.FromResult(new PlanProposal
                {
                    Options = [new PlanOption { Summary = "try somewhere else", Recommended = true }]
                });
            },
            new SupervisionOptions(),
            state => state.Coordination,
            (state, c) => state with { Coordination = c });

        await job.ExecuteAsync(new Held { Coordination = new PlanCoordination { Result = result } });

        asked.ShouldBe(1);
    }

    // ── Fixtures ──

    private sealed record Held
    {
        public PlanCoordination? Coordination { get; init; }
    }

    private static async Task<PlanRunResult> Running(
        PlanNodeRunner runner, PlanRetryPolicy? policy = null)
    {
        var store = new InMemoryPlanTreeStore();

        await store.SaveAsync(PlanTree.Create(
            "plan",
            new AgentContract { Goal = "Ship it", AcceptanceCriteria = ["it ships"] },
            [("step", new AgentContract { Goal = "Do the step", AcceptanceCriteria = ["it is done"] })]))
            .ConfigureAwait(false);

        return await new PlanExecutor(store, retries: policy)
            .ExecuteAsync("plan", runner)
            .ConfigureAwait(false);
    }

    private sealed class Sink(List<PlanNodeFailed> seen) : IWorkflowEventSink
    {
        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            if (evt is PlanNodeFailed failed)
                seen.Add(failed);

            return ValueTask.CompletedTask;
        }
    }
}
