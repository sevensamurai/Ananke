using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A plan running as one job in an ordinary workflow.
/// </summary>
/// <remarks>
/// <para>
/// The claim under test is that this is <b>not a second execution model</b>. So the tests that
/// matter are the ones that would fail if it quietly became one: a plan that runs to completion with
/// no loop written by the caller, a dispute that stops and is <em>returned</em> rather than resolved,
/// and sequential execution inside a plan that survives a workflow forking two of them.
/// </para>
/// <para>
/// The workflow state carrying a plan from one job to the next is deliberately exercised, because
/// mistaking that for a node handoff is what kept this design from being a job in the first place.
/// </para>
/// </remarks>
[TestFixture]
public class SupervisedJobTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private sealed record DeliveryState
    {
        public PlanTree? Plan { get; init; }
        public PlanRunResult? Outcome { get; init; }
    }

    // ── It is an ordinary job ──

    [Test]
    public async Task Supervise_RunsThePlanToCompletion_WithNoLoopWrittenByTheCaller()
    {
        // Every node fails its first attempt and passes its second. One pass is not enough, and
        // nothing in this test repeats anything.
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);

        var result = await Delivery(Supervision((context, _) =>
        {
            var attempt = attempts[context.Node.Id] = attempts.GetValueOrDefault(context.Node.Id) + 1;
            return Task.FromResult(Answer(context, passed: attempt > 1));
        })).RunAsync(new DeliveryState());

        result.State.Outcome!.RootOutcome.ShouldBe(ContractOutcome.Met);
        result.State.Outcome.Tree.Node("plan-parse").ReadCount.ShouldBe(2);
    }

    [Test]
    public async Task Supervise_TheStateFromAnEarlierJob_IsWhereThePlanComesFrom()
    {
        // R2 in one test: the plan reaches the supervised job through ordinary workflow state,
        // written by an ordinary earlier job. That is not the handoff the tree replaces.
        var workflow = new Workflow<DeliveryState>("delivery")
            .Job("decompose", (s, _) => Task.FromResult(s with { Plan = Tree() }))
            .Supervise("deliver", s => s.Plan!, Supervision(Meets), (s, r) => s with { Outcome = r })
            .Chain("decompose", "deliver")
            .Then("deliver", Workflow.End);

        var result = await workflow.RunAsync(new DeliveryState());

        result.State.Outcome!.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task Supervise_IsAnOrdinaryJob_OnTheEventStream()
    {
        var events = new List<WorkflowEvent>();
        await foreach (var evt in Delivery(Supervision(Meets)).StreamAsync(new DeliveryState()))
            events.Add(evt);

        events.OfType<JobStarted<DeliveryState>>().Select(e => e.JobName).ShouldContain("deliver");
        events.OfType<JobCompleted<DeliveryState>>().Select(e => e.JobName).ShouldContain("deliver");
    }

    // ── What it must never do ──

    [Test]
    public async Task Supervise_AStandingDispute_StopsTheRunAndIsReturnedUnresolved()
    {
        // A dispute reaches the tree through DisputeAsync now — an external reviewer's call, never a
        // node's own report (R26). What must still be true: nothing resolves it on the node's behalf.
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree());
        await new PlanExecutor(store).DisputeAsync(
            "plan", "plan-parse", "the parser round-trips",
            "round-tripping is impossible for the input format as specified");

        var supervision = Supervision((context, ct) =>
            context.Node.Id == "plan-parse"
                ? Task.FromResult(NodeOutcome.Nothing)
                : Meets(context, ct)) with
        { Store = store };

        var result = await Delivery(supervision).RunAsync(new DeliveryState());

        var outcome = result.State.Outcome!;

        outcome.HaltedAt.ShouldBe("plan-parse");
        outcome.RootOutcome.ShouldBe(ContractOutcome.Disputed);

        // Nothing re-ruled on the node's behalf: the dispute still stands in the tree and the plan
        // is still on its first version. Deciding whether the contract is wrong belongs above this.
        outcome.Tree.Node("plan-parse").Violation.ShouldNotBeNull();
        outcome.Tree.Lineage.Count.ShouldBe(1);
    }

    [Test]
    public async Task Supervise_TwoPlansForkedInOneWorkflow_EachStillRunsOneNodeAtATime()
    {
        // R5: forking two supervised jobs is what workflows do. It does not widen what happens
        // inside either plan — each one still runs one node at a time.
        var inFlight = new Dictionary<string, int>(StringComparer.Ordinal) { ["left"] = 0, ["right"] = 0 };
        var samePlanOverlap = false;
        var plansOverlapped = false;
        var gate = new Lock();

        async Task<NodeOutcome> Watched(PlanNodeContext context, CancellationToken ct)
        {
            var plan = context.Node.Id.Split('-')[0];

            lock (gate)
            {
                inFlight[plan]++;
                if (inFlight[plan] > 1)
                    samePlanOverlap = true;
                if (inFlight["left"] > 0 && inFlight["right"] > 0)
                    plansOverlapped = true;
            }

            await Task.Delay(5, ct);

            lock (gate)
                inFlight[plan]--;

            return Answer(context, passed: true);
        }

        var workflow = new Workflow<DeliveryState>("two-plans")
            .Job("start", (s, _) => Task.FromResult(s))
            .Supervise("left-plan", _ => Tree("left"), Supervision(Watched), (s, _) => s)
            .Supervise("right-plan", _ => Tree("right"), Supervision(Watched), (s, _) => s)
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("left-plan", "right-plan"))
            .Join(["left-plan", "right-plan"], "merge", states => states[0])
            .Then("merge", Workflow.End);

        await workflow.RunAsync(new DeliveryState());

        // The guard first: without this the assertion below would hold for a workflow that ran the
        // two plans one after the other, which proves nothing about R12.
        plansOverlapped.ShouldBeTrue("the fork ran both plans at once, which is what a fork is for");
        samePlanOverlap.ShouldBeFalse("execution within a plan is sequential, fork or no fork");
    }

    // ── The store ──

    [Test]
    public async Task Supervise_APlanTheWorkflowAlreadyHas_NeedsNoPlanInTheState()
    {
        // The tree belongs to the supervised job, not to the state travelling between jobs. A
        // workflow whose state never mentions a plan still supervises one.
        var workflow = new Workflow<DeliveryState>("delivery")
            .Supervise("deliver", Tree(), Supervision(Meets), (s, r) => s with { Outcome = r })
            .Then("deliver", Workflow.End);

        var result = await workflow.RunAsync(new DeliveryState());

        result.State.Plan.ShouldBeNull();
        result.State.Outcome!.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task Supervise_TwoJobsOverOnePlanAndOneStore_TheSecondPicksUpWhereTheFirstStopped()
    {
        // What made a second supervised job necessary before: the plan id and the store are the
        // identity of the run, and neither of them is workflow state.
        var store = new InMemoryPlanTreeStore();
        var ran = new List<string>();
        var supervision = Supervision((context, ct) =>
        {
            ran.Add(context.Node.Id);
            return Meets(context, ct);
        }) with
        { Store = store };

        var workflow = new Workflow<DeliveryState>("delivery")
            .Supervise("first", Tree(), supervision, (s, r) => s with { Outcome = r })
            .Supervise("second", Tree(), supervision, (s, r) => s with { Outcome = r })
            .Chain("first", "second")
            .Then("second", Workflow.End);

        await workflow.RunAsync(new DeliveryState());

        ran.Count(id => id == "plan-parse").ShouldBe(1, "the second job read the store, not the tree it was handed");
    }

    [Test]
    public async Task Supervise_TwoJobsWithNoStoreConfigured_StillSuperviseOnePlan()
    {
        // Where the tree lives when nobody says is still one place. Two jobs each keeping their own
        // would run the same plan twice and call it progress.
        var ran = new List<string>();
        var supervision = Supervision((context, ct) =>
        {
            ran.Add(context.Node.Id);
            return Meets(context, ct);
        });

        var workflow = new Workflow<DeliveryState>("delivery")
            .Supervise("first", Tree(), supervision, (s, r) => s with { Outcome = r })
            .Supervise("second", Tree(), supervision, (s, r) => s with { Outcome = r })
            .Chain("first", "second")
            .Then("second", Workflow.End);

        await workflow.RunAsync(new DeliveryState());

        ran.Count(id => id == "plan-parse").ShouldBe(1);
    }

    [Test]
    public async Task Supervise_OptionsCopiedWithAnAdjustment_StillShareTheOnePlan()
    {
        // `with` copies the resolved store rather than making a second one. Two jobs configured
        // slightly differently are still supervising one plan, which is what a copy means here.
        var ran = new List<string>();
        var supervision = Supervision((context, ct) =>
        {
            ran.Add(context.Node.Id);
            return Meets(context, ct);
        });

        var workflow = new Workflow<DeliveryState>("delivery")
            .Supervise("first", Tree(), supervision, (s, r) => s with { Outcome = r })
            .Supervise("second", Tree(), supervision with { ProjectionTokenBudget = 100 },
                (s, r) => s with { Outcome = r })
            .Chain("first", "second")
            .Then("second", Workflow.End);

        await workflow.RunAsync(new DeliveryState());

        ran.Count(id => id == "plan-parse").ShouldBe(1);
    }

    [Test]
    public async Task Rerule_UnderTheSameSupervision_ChangesThePlanTheSupervisedJobReads()
    {
        // The job that decides a plan should change is the consumer's, and it should not have to
        // assemble an executor to say so — nor risk re-ruling a different store from the one being
        // supervised.
        var supervision = Supervision(Meets);
        await Delivery(supervision).RunAsync(new DeliveryState());

        await supervision.ReruleAsync(
            "plan", "plan-render", Contract("Render it differently", "it renders"), "the format changed");

        var result = await Delivery(supervision).RunAsync(new DeliveryState());

        result.State.Outcome!.Tree.Lineage.Count.ShouldBe(2);
        result.State.Outcome.Tree.Node("plan-render").Contract.Goal.ShouldBe("Render it differently");
    }

    [Test]
    public async Task Supervise_AStoreThatAlreadyHoldsThePlan_ResumesRatherThanStartingAgain()
    {
        var store = new InMemoryPlanTreeStore();

        // A previous run got "parse" done and died before the rest.
        var partly = Tree().WithVerdict("plan-parse", Verdict("the parser round-trips", passed: true));
        await store.SaveAsync(partly);

        var ran = new List<string>();
        var supervision = Supervision((context, ct) =>
        {
            ran.Add(context.Node.Id);
            return Meets(context, ct);
        }) with
        { Store = store };

        // The state carries the plan as it was before any of that happened.
        await Delivery(supervision).RunAsync(new DeliveryState());

        ran.ShouldNotContain("plan-parse", "the store held it satisfied already");
        ran.ShouldContain("plan-render");
    }

    [Test]
    public async Task Supervise_WithNoStoreConfigured_KeepsTheTreeSomewhereOfItsOwn()
    {
        var result = await Delivery(Supervision(Meets)).RunAsync(new DeliveryState());

        result.State.Outcome!.Tree.Node("plan-parse").Verdicts.ShouldNotBeEmpty();
    }

    [Test]
    public async Task Supervise_GivenNoPlan_SaysSoRatherThanRunningNothing()
    {
        // Was written as a discarded ThrowAsync and asserted nothing whatever the code did. A
        // workflow does not throw a job's exception — it reports it — so this is where it shows up.
        var result = await Delivery(Supervision(Meets), plan: _ => null!).RunAsync(new DeliveryState());

        result.IsFailure.ShouldBeTrue();
        result.Result!.Exception.ShouldBeOfType<InvalidOperationException>();
        result.Result.Exception!.Message.ShouldContain("was given no plan");
    }

    // ── Models by role ──

    [Test]
    public async Task Supervise_AnExecutorModelAndNoRunner_RunsTheNodeWithoutSettlingIt()
    {
        // No runner delegate, no runner options, and nothing implemented by the consumer — but a
        // node's own report is narration only now (R26), so an Executor alone can run the work and
        // can never settle it. Only a verifier's ruling can.
        var supervision = new SupervisionOptions { Executor = Reports() };

        var result = await Delivery(supervision).RunAsync(new DeliveryState());

        result.State.Outcome!.RootOutcome.ShouldBe(ContractOutcome.Unmet);
        result.State.Outcome.Tree.Node("plan-parse").LastRead.ShouldNotBeNull("the model still ran");
        result.State.Outcome.Tree.Node("plan-parse").Verdicts
            .ShouldBeEmpty("a report is narration, never a verdict");
    }

    [Test]
    public async Task Supervise_ARunnerAndAnExecutor_UsesTheRunner()
    {
        // A node is not necessarily an agent, so the delegate has to win — otherwise configuring a
        // model would quietly disable the shell command or the person already doing the work.
        var ran = false;

        var supervision = new SupervisionOptions
        {
            Executor = Reports(),
            Runner = (context, ct) =>
            {
                ran = true;
                return Meets(context, ct);
            }
        };

        var result = await Delivery(supervision).RunAsync(new DeliveryState());

        ran.ShouldBeTrue();
        result.State.Outcome!.RootOutcome.ShouldBe(ContractOutcome.Met);
    }

    [Test]
    public async Task Supervise_NeitherARunnerNorAnExecutor_SaysWhatIsMissing()
    {
        // A workflow reports a job's failure rather than throwing it, so the message has to be
        // readable from the result — which is the only place anyone will see it.
        var result = await Delivery(new SupervisionOptions()).RunAsync(new DeliveryState());

        result.IsFailure.ShouldBeTrue();
        result.Result!.Exception.ShouldBeOfType<InvalidOperationException>();
        result.Result.Exception!.Message.ShouldContain("Executor");
        result.Result.Exception.Message.ShouldContain("Runner");
    }

    [Test]
    public void ModelFor_ARoleNamedOnlyInTheMap_IsFound()
    {
        var reviewer = Reports();

        var supervision = new SupervisionOptions
        {
            Models = new Dictionary<string, IAgentModel>(StringComparer.OrdinalIgnoreCase)
            {
                ["reviewer"] = reviewer
            }
        };

        supervision.ModelFor("reviewer").ShouldBeSameAs(reviewer);
        supervision.ModelFor("REVIEWER").ShouldBeSameAs(reviewer, "roles are names, not identifiers");
        supervision.ModelFor(PlanRoles.Supervisor).ShouldBeNull();
    }

    [Test]
    public void ModelFor_ARoleGivenBothWays_PrefersTheNamedProperty()
    {
        // The visible one should not be the one that loses silently.
        var sugar = Reports();

        var supervision = new SupervisionOptions
        {
            Supervisor = sugar,
            Models = new Dictionary<string, IAgentModel>(StringComparer.OrdinalIgnoreCase)
            {
                [PlanRoles.Supervisor] = Reports()
            }
        };

        supervision.ModelFor(PlanRoles.Supervisor).ShouldBeSameAs(sugar);
    }

    [Test]
    public void ResolvedRunner_AfterAWithCopyThatChangesTheExecutor_RunsAgainstTheNewModel()
    {
        // A materialised runner shared across `with` copies would answer for the model the options
        // used to name — the opposite of the store, where sharing is the point.
        var supervision = new SupervisionOptions { Executor = Reports() };
        var replaced = supervision with { Executor = Reports() };

        supervision.ModelFor(PlanRoles.Executor).ShouldNotBeSameAs(replaced.ModelFor(PlanRoles.Executor));
    }

    // ── Fixtures ──

    private static Workflow<DeliveryState> Delivery(
        SupervisionOptions supervision, Func<DeliveryState, PlanTree>? plan = null) =>
        new Workflow<DeliveryState>("delivery")
            .Supervise("deliver", plan ?? (_ => Tree()), supervision, (s, r) => s with { Outcome = r })
            .Then("deliver", Workflow.End);

    private static SupervisionOptions Supervision(PlanNodeRunner runner) => new() { Runner = runner };

    private static PlanTree Tree(string planId = "plan") => PlanTree.Create(
        planId,
        Contract("Ship it", "the build is green"),
        [
            ($"{planId}-parse", Contract("Parse", "the parser round-trips")),
            ($"{planId}-render", Contract("Render", "output matches the golden file"))
        ]);

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static Task<NodeOutcome> Meets(PlanNodeContext context, CancellationToken ct) =>
        Task.FromResult(Answer(context, passed: true));

    private static NodeOutcome Answer(PlanNodeContext context, bool passed) => new()
    {
        Verdicts = [.. context.Contract.AcceptanceCriteria.Select(c => Verdict(c, passed))]
    };

    private static CriterionVerdict Verdict(string criterion, bool passed) =>
        new() { Criterion = criterion, Passed = passed, Oracle = "dotnet test", At = T0 };

    /// <summary>A model that answers every node with the same report.</summary>
    private static IAgentModel Reports() => SimulatedAgentModel.Json(new PlanNodeReport
    {
        Summary = "did the work"
    });
}
