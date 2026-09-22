using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The seam between a plan node and an agent job.
/// </summary>
/// <remarks>
/// <para>
/// Both sides of this join were already built and tested — a contract is pinned by construction
/// inside a job, and nothing crosses a node boundary except through the tree. What had never been
/// pinned is that either property survives the join, because until this runner existed there was no
/// join to test: every consumer wrote it by hand.
/// </para>
/// <para>
/// A node's own report no longer becomes a verdict, a dispute or a question (R26) — <c>ToOutcome</c>
/// and the guards that arbitrated a self-report are gone with it. What is left worth testing here is
/// narrower: that the seam still pins the contract correctly, gives each node a fresh job, and carries
/// a node's <c>Summary</c> to the event stream untouched.
/// </para>
/// </remarks>
[TestFixture]
public class PlanNodeAgentRunnerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    private const string Criterion = "the parser round-trips";

    // ── Pinning across the seam ──

    [Test]
    public async Task RunAsync_Always_PinsTheNodesContractIntoTheRequest()
    {
        var model = Model(Report());

        await Runner(model).RunAsync(Context());

        var systemPrompt = model.Requests[^1].SystemPrompt;
        systemPrompt.ShouldNotBeNull();
        systemPrompt.ShouldContain("Parse the input");
        systemPrompt.ShouldContain(Criterion);
    }

    [Test]
    public async Task RunAsync_WhenConfigureSetsItsOwnContract_TheNodesContractStillWins()
    {
        var model = Model(Report());
        var options = Options(model) with
        {
            Configure = b => b.WithContract(new AgentContract { Goal = "Something else entirely" })
        };

        await new PlanNodeAgentRunner(options).RunAsync(Context());

        // A caller that could overwrite the contract could unpin it, and R2 would hold only for
        // callers who remembered.
        model.Requests[^1].SystemPrompt!.ShouldContain("Parse the input");
        model.Requests[^1].SystemPrompt!.ShouldNotContain("Something else entirely");
    }

    [Test]
    public async Task RunAsync_WithAPersona_KeepsItSeparateFromTheContract()
    {
        var model = Model(Report());

        await new PlanNodeAgentRunner(Options(model) with { Persona = "You are terse." })
            .RunAsync(Context());

        var systemPrompt = model.Requests[^1].SystemPrompt!;
        systemPrompt.ShouldContain("You are terse.");
        systemPrompt.IndexOf("You are terse.", StringComparison.Ordinal)
            .ShouldBeLessThan(systemPrompt.IndexOf("# Contract", StringComparison.Ordinal));
    }

    [Test]
    public async Task RunAsync_TheTreeView_GoesOnTheUserTurnAndNotIntoThePinnedSection()
    {
        var model = Model(Report());
        var context = Context() with { TreeView = "[parse] Parse the input — no verdict yet" };

        await Runner(model).RunAsync(context);

        // P6, pinned by a test rather than by a comment: the projection is a reading of a plan
        // version, already budgeted and already trimmed. Pinning it would pin something that is
        // true only of the moment the node started.
        model.Requests[^1].SystemPrompt!.ShouldNotContain("no verdict yet");
        model.Requests[^1].Messages[0].Content!.ShouldContain("no verdict yet");
    }

    [Test]
    public async Task RunAsync_TwoNodesInARow_DoNotShareAContract()
    {
        var model = Model(Report(), Report());
        var runner = Runner(model);

        await runner.RunAsync(Context());
        await runner.RunAsync(Context() with
        {
            Contract = new AgentContract { Goal = "Render the output" },
            Node = new PlanNode { Id = "render", Contract = new AgentContract { Goal = "Render the output" } }
        });

        model.Requests[^1].SystemPrompt!.ShouldContain("Render the output");
        model.Requests[^1].SystemPrompt!.ShouldNotContain("Parse the input");
    }

    // ── What a node's report carries, and what it cannot ──

    [Test]
    public async Task RunAsync_ANodesOwnReport_NeverBecomesAVerdict()
    {
        // R26: a step says what it did; nothing derives from it. Before this runner stopped asking a
        // model to grade its own work, a report like this one was exactly how a node talked its way
        // to Satisfied.
        var outcome = await Runner(Model(Report())).RunAsync(Context());

        outcome.Verdicts.ShouldBeEmpty();
        outcome.Summary.ShouldBe("parsed it");
    }

    [Test]
    public async Task RunAsync_AStepThatCouldNotDoItsTask_CarriesThatAndItsOptions()
    {
        var reply = JsonSerializer.Serialize(new PlanNodeReport
        {
            Summary = "no stay in hakone is free for 2 nights",
            Done = false,
            Options = ["onsen-ryokan: 1 night, Wed 7 Apr"]
        });

        var outcome = await Runner(Model(reply)).RunAsync(Context());

        outcome.Done.ShouldBeFalse();
        outcome.Options.ShouldBe(["onsen-ryokan: 1 night, Wed 7 Apr"]);
        outcome.Candidate.ShouldBeNull();
    }

    [Test]
    public async Task RunAsync_AStepThatDidItsTask_ReportsDoneAndNoOptions()
    {
        var outcome = await Runner(Model(Report())).RunAsync(Context());

        outcome.Done.ShouldBeTrue();
        outcome.Options.ShouldBeEmpty();
    }

    [Test]
    public void BuildPrompt_AsksWhetherTheTaskCouldBeDone_AndForOptionsWhenItCouldNot()
    {
        var catalog = new OperationCatalog(
            [new OperationDefinition { Name = "parse", Arity = 0, Description = "parse the input" }]);

        var prompt = new PlanNodeAgentRunner(Options(Model(Report())) with { Operations = catalog })
            .BuildPrompt(Context());

        prompt.ShouldContain("If you could not do it");
        prompt.ShouldContain("\"Options\"");
    }

    // A proposing step's contract cannot be met while it proposes — nothing has applied the change
    // or checked it — so read the other way it reports not-done and PlanExecutor drops the candidate
    // it just made. Four live runs went by before that was found, and the only thing standing
    // between it and a return is this wording.

    [Test]
    public void BuildPrompt_WithOperations_TellsAProposingStepThatProposingIsDoingItsPart()
    {
        var catalog = new OperationCatalog(
            [new OperationDefinition { Name = "parse", Arity = 0, Description = "parse the input" }]);

        var prompt = new PlanNodeAgentRunner(Options(Model(Report())) with { Operations = catalog })
            .BuildPrompt(Context());

        prompt.ShouldContain("Proposing a change is doing your part");
        prompt.ShouldContain("When you propose a change, leave \"Options\" empty");
    }

    [Test]
    public void BuildPrompt_WithNoOperations_SaysNeitherOfThose()
    {
        var prompt = Runner(Model(Report())).BuildPrompt(Context());

        // "If nothing comes close, leave \"Options\" empty" is the shared paragraph's, and every
        // domain gets it. What must not reach a step with no catalog is the proposing half.
        prompt.ShouldNotContain("doing your part");
        prompt.ShouldNotContain("When you propose a change");
    }

    [Test]
    public void BuildPrompt_WithNoOperations_AsksWhatWasFoundAndNeverForAChange()
    {
        var prompt = Runner(Model(Report())).BuildPrompt(Context());

        prompt.ShouldContain("\"Done\"");
        prompt.ShouldContain("\"Options\"");
        prompt.ShouldNotContain("Propose");
        prompt.ShouldNotContain("propose");
    }

    [Test]
    public async Task ARunNodesSummary_ReachesTheEventStream()
    {
        var sink = new CollectingSink();
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan", Contract("Ship it"), [("parse", Contract("Parse the input", Criterion))]));

        using (WorkflowEventReporting.BeginScope(sink))
            await new PlanExecutor(store).ExecuteAsync("plan", Runner(Model(Report())).AsRunner());

        // The summary is the one part of a report the tree deliberately has no room for, so if it
        // is not reported it reaches nobody. Reported by the executor from the outcome, which is why
        // a runner that is a program or a person gets the same line as one that is an agent.
        //
        // A report is narration only now (R26), so "parse" never settles from its own say-so and the
        // root — which declares no criteria of its own — runs too and reports as well; this asserts
        // on "parse" specifically rather than assuming it is the only node that ran.
        var reported = sink.Events.OfType<PlanNodeReported>().Single(e => e.NodeId == "parse");
        reported.Summary.ShouldBe("parsed it");
        reported.PlanId.ShouldBe("plan");
    }

    [Test]
    public async Task ANodeThatIsNotAnAgent_ReportsItsSummaryTheSameWay()
    {
        // The gap this closes: reporting used to live inside the agent runner, so a plan whose steps
        // are programs, commands or people ran silently — every narration of it read as a plan that
        // had done nothing.
        var sink = new CollectingSink();
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan", Contract("Ship it"), [("parse", Contract("Parse the input", Criterion))]));

        using (WorkflowEventReporting.BeginScope(sink))
        {
            await new PlanExecutor(store).ExecuteAsync(
                "plan",
                (_, _) => Task.FromResult(new NodeOutcome { Summary = "ran a script" }));
        }

        // Two, and both are right. This runner summarises whatever it is handed and satisfies
        // nothing, so `parse` does not settle and the root runs too — and a root is a node with a
        // contract like any other. What the test is about is the runner's *shape*: a step that is a
        // program gets the line an agent would have got, which is what reporting from the executor
        // rather than from inside the agent runner buys.
        var reported = sink.Events.OfType<PlanNodeReported>().ToList();

        reported.Select(e => e.NodeId).ShouldBe(["parse", "plan"]);
        reported.ShouldAllBe(e => e.Summary == "ran a script");
    }

    [Test]
    public async Task RunAsync_WithNobodyListening_StillRuns()
    {
        // No scope, no sink. A node that could only run inside a stream would be unusable under
        // RunAsync, which is most of how a plan gets run.
        var outcome = await Runner(Model(Report())).RunAsync(Context());

        outcome.Summary.ShouldBe("parsed it");
    }

    // ── Fixtures ──

    private static PlanNodeAgentRunner Runner(SimulatedAgentModel model) => new(Options(model));

    private static PlanNodeAgentOptions Options(SimulatedAgentModel model) => new()
    {
        Model = model,
        TimeProvider = new FixedTime(T0)
    };

    private static PlanNodeContext Context()
    {
        var contract = Contract("Parse the input", Criterion);
        return new PlanNodeContext
        {
            PlanId = "plan",
            PlanVersion = 1,
            Node = new PlanNode { Id = "parse", Contract = contract },
            Contract = contract,
            TreeView = "[parse] Parse the input",
            Projection = new PlanTreeProjection.Projection
            {
                Text = "[parse] Parse the input",
                EstimatedTokens = 8
            }
        };
    }

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static string Report() => JsonSerializer.Serialize(new PlanNodeReport
    {
        Summary = "parsed it"
    });

    private static SimulatedAgentModel Model(params string[] responses) =>
        SimulatedAgentModel.Sequence(responses);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CollectingSink : IWorkflowEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
