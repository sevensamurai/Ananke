using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Planning;
using System.Reflection;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Attended and unattended are one lane: the loop does not know which it is in.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this pins.</b> A run that answers its own questions and a run that stops to ask are the
/// same jobs over the same edges. What differs is who answers — and, downstream of that, what the
/// round is charged. Nothing else may differ, because two lanes would mean the unattended path is a
/// second implementation of the loop, exercised by different runs and free to drift from the first.
/// </para>
/// <para>
/// <b>Why it is worth a test rather than a rule.</b> The two paths were two workflows until
/// 2026-09-05, and the run that minted twenty-one plan versions without stopping happened on the one
/// with no person in it. A fork is easy to reintroduce and impossible to see in a diff — the second
/// branch reads perfectly well on its own.
/// </para>
/// <para>
/// <b>The pause is declared either way.</b> Autopilot does not remove it; it answers before the
/// arrival, so the condition that would have stopped the run — <em>a question is outstanding</em> —
/// is simply false. One condition covering both modes rather than a condition per mode.
/// </para>
/// </remarks>
[TestFixture]
public class AttendedAndUnattendedTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The criterion no arrangement can satisfy, and the only thing that halts this plan.</summary>
    private const string Impossible = "day-3 fits within the party's daily limit";

    private sealed record Trip
    {
        public PlanCoordination? Coordination { get; init; }
    }

    [Test]
    public void Build_WithAndWithoutAutopilot_ProducesIdenticalTopology()
    {
        var unattended = Wire(autopilot: true).Build();
        var attended = Wire(autopilot: false).Build();

        Topology(unattended).ShouldBe(Topology(attended));
    }

    [Test]
    public void Build_WithAutopilot_StillDeclaresThePause()
    {
        // The flag answers the question; it does not remove the place where somebody would be asked.
        // A workflow that dropped the pause under autopilot would be the second lane in miniature —
        // and could never be run attended without rebuilding it.
        Wire(autopilot: true).Build().InputJobs.ShouldContain("choose");
        Wire(autopilot: false).Build().InputJobs.ShouldContain("choose");
    }


    // ── The experiment: the same halt, answered two ways ──

    [Test]
    public async Task Run_AutopilotAndPersonPickingTheRecommendation_ProduceTheSameLineage()
    {
        // R14 as an experiment. If attended and unattended are one lane, then a person who picks
        // what the supervisor recommended is doing exactly what autopilot does — so the plan that
        // comes out the far end must be the same plan, node for node and version for version.
        var unattended = await Wire(autopilot: true).RunAsync(new Trip());

        var attended = await Answered(Wire(autopilot: false));

        unattended.Status.ShouldBe(ExecutionStatus.Completed);
        attended.Status.ShouldBe(ExecutionStatus.Completed);

        // The comparison says nothing unless both runs actually changed the plan. Two identical
        // runs that each did nothing would pass the assertion below and prove no lane was joined.
        var tree = unattended.State.Coordination!.Result.Tree;
        tree.Lineage.Count.ShouldBe(2);
        tree.Current.ReRuledNodeId.ShouldBe("family-trip");
        tree.Node("day-3").Contract!.Goal.ShouldBe("Plan the island over two days");
        unattended.State.Coordination.Result.HaltedAt.ShouldBeNull();

        Lineage(unattended.State).ShouldBe(Lineage(attended.State));
    }

    [Test]
    public async Task Run_Attended_StopsAtTheQuestionAndNotBefore()
    {
        // The other half of the same claim: the pause is real when nobody answered. If this ever
        // completes without interrupting, the comparison above is comparing a run against itself.
        var paused = await Wire(autopilot: false).RunAsync(new Trip());

        paused.Status.ShouldBe(ExecutionStatus.Interrupted);
        paused.CurrentJob.ShouldBe("choose");
        paused.State.Coordination!.Question!.Outstanding.ShouldBeTrue();
    }

    [Test]
    public async Task Run_Unattended_NeverStops()
    {
        // And autopilot does not reach the pause at all — not because the pause is gone, but
        // because the question it stops for was answered before the arrival.
        var run = await Wire(autopilot: true).RunAsync(new Trip());

        run.Status.ShouldBe(ExecutionStatus.Completed);
        run.State.Coordination!.Question.ShouldBeNull();
    }



    // ── One reader of the flag ──

    [Test]
    public void PublicSurface_MentionsAutopilotInExactlyOnePlace()
    {
        // Whatever puts the question owns the flag. Everything after it sees a question that is
        // answered or one that is not, and must not be able to tell who answered — a second reader
        // is how a branch on the mode gets back in, one honest-looking `if` at a time.
        var readers = Readers().ToList();

        readers.Select(reader => reader.Owner).Distinct().ShouldBe([typeof(PlanAskingJob<>)]);

        // Named, so a failure says which member reintroduced it rather than only that one did.
        readers.Select(reader => reader.Where).ShouldBe(["PlanAskingJob`1..ctor(autopilot)"]);
    }

    /// <summary>
    /// Every public thing in the orchestration assembly that names the flag.
    /// </summary>
    /// <remarks>
    /// <b>Public surface only, which is the scan that means something.</b> A closure capturing the
    /// parameter compiles into a private field named after it, so a scan that reached non-public
    /// members would fail on the one place the flag is allowed to live. What a second lane needs is
    /// a way for other code to see the mode, and that has to be public to be of any use.
    /// </remarks>
    private static IEnumerable<(Type Owner, string Where)> Readers()
    {
        foreach (var type in typeof(PlanAskingJob<>).Assembly.GetExportedTypes())
        {
            foreach (var ctor in type.GetConstructors())
                foreach (var parameter in ctor.GetParameters().Where(Names))
                    yield return (type, $"{type.Name}..ctor({parameter.Name})");

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance
                                                   | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (Names(method))
                    yield return (type, $"{type.Name}.{method.Name}()");

                foreach (var parameter in method.GetParameters().Where(Names))
                    yield return (type, $"{type.Name}.{method.Name}({parameter.Name})");
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance
                                                        | BindingFlags.Static | BindingFlags.DeclaredOnly)
                                         .Where(Names))
                yield return (type, $"{type.Name}.{property.Name}");
        }
    }

    private static bool Names(MemberInfo member) => Names(member.Name);

    private static bool Names(ParameterInfo parameter) => Names(parameter.Name);

    private static bool Names(string? name) =>
        name?.Contains("autopilot", StringComparison.OrdinalIgnoreCase) == true;

    // ── The one difference the ruling permits ──

    [Test]
    public async Task Run_TheRoundAutopilotAnswered_IsChargedAndTheOneAPersonAnsweredIsNot()
    {
        // The only thing the two runs are allowed to disagree about. A pause is bounded by somebody
        // being there, so a round a person answered costs the change-of-plan budget nothing.
        // Autopilot is the case where nobody is — and an unattended run with no such bound minted
        // twenty-one plan versions and would not have stopped.
        var unattended = await Wire(autopilot: true).RunAsync(new Trip());
        var attended = await Answered(Wire(autopilot: false));

        unattended.State.Coordination!.Changes.ShouldBe(1);
        attended.State.Coordination!.Changes.ShouldBe(0);

        // And it is the same change of plan either way. The budget is charged differently; nothing
        // about what was decided is.
        Lineage(unattended.State).ShouldBe(Lineage(attended.State));
    }

    [Test]
    public async Task Run_WhoAnsweredIsRecorded_AndIsTheOnlyThingTheDecisionEventDisagreesAbout()
    {
        // Provenance rather than a lane. A reader has to be able to tell a round the run answered
        // itself from one somebody was stopped and asked for — and it is read off how the job was
        // entered, not declared by whoever wrote the coordinator.
        var unattended = await DecisionsFrom(() => Wire(autopilot: true).RunAsync(new Trip()));
        var attended = await DecisionsFrom(() => Answered(Wire(autopilot: false)));

        unattended.Count.ShouldBe(1);
        attended.Count.ShouldBe(1);

        unattended[0].Escalated.ShouldBeFalse();
        attended[0].Escalated.ShouldBeTrue();

        // Everything else the event carries is the same halt, the same node and the same decision.
        attended[0].NodeId.ShouldBe(unattended[0].NodeId);
        attended[0].PlanVersion.ShouldBe(unattended[0].PlanVersion);
        attended[0].Decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
        unattended[0].Decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
    }

    /// <summary>Every decision the run reported, in order.</summary>
    private static async Task<List<PlanDecisionTaken>> DecisionsFrom(
        Func<Task<WorkflowExecution<Trip>>> run)
    {
        var sink = new CollectingSink();

        using (WorkflowEventReporting.BeginScope(sink))
            await run().ConfigureAwait(false);

        return [.. sink.Events.OfType<PlanDecisionTaken>()];
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

    /// <summary>Runs an attended workflow and answers its question with the recommendation.</summary>
    /// <remarks>
    /// <b>Read off the question rather than hardcoded.</b> "The person picked the recommendation" has
    /// to mean the same thing the autopilot means by it, or the two runs are not comparable.
    /// </remarks>
    private static async Task<WorkflowExecution<Trip>> Answered(Workflow<Trip> workflow)
    {
        var run = await workflow.RunAsync(new Trip()).ConfigureAwait(false);

        while (run.Status == ExecutionStatus.Interrupted)
        {
            var question = run.State.Coordination!.Question!;
            var recommended = question.Options.ToList().FindIndex(option => option.Recommended);

            run = await workflow.ResumeAsync(run.Id, paused => paused with
            {
                Coordination = paused.Coordination! with
                {
                    Question = paused.Coordination.Question! with { Picked = recommended + 1 }
                }
            }).ConfigureAwait(false);
        }

        return run;
    }

    /// <summary>
    /// The plan that came out: every version, every node, and what the record says about it.
    /// </summary>
    /// <remarks>
    /// <b>Timestamps are left out, and nothing else is.</b> Two runs happen at two moments, so
    /// <c>MintedAt</c> and the verdict clocks differ by construction and say nothing about whether
    /// the lane forked. Everything a second lane could plausibly change — the number of versions,
    /// which node each re-ruled and why, the tree's shape, every contract, and every node's status —
    /// is compared.
    /// </remarks>
    private static string Lineage(Trip state)
    {
        var tree = state.Coordination!.Result.Tree;

        return string.Join(
            "\n",
            (string[])
            [
                $"plan={tree.PlanId} versions={tree.Lineage.Count} halted={state.Coordination.Result.HaltedAt ?? "nothing"}",
                .. tree.Lineage.Select(version =>
                    $"v{version.Number} reruled={version.ReRuledNodeId ?? "-"} "
                    + $"reason={version.Reason ?? "-"} by={version.Rationale?.By ?? "-"}"),
                .. tree.Current.Nodes
                    .OrderBy(node => node.Key, StringComparer.Ordinal)
                    .Select(node =>
                        $"node {node.Key} parent={node.Value.ParentId ?? "-"} "
                        + $"outcome={tree.OutcomeOf(node.Key)} goal={node.Value.Contract!.Goal} "
                        + $"criteria=[{string.Join("|", node.Value.Contract!.AcceptanceCriteria)}]")
            ]);
    }

    // ── What "the same topology" is compared as ──

    /// <summary>
    /// Everything about a definition that a second lane would have to change: the jobs, how each one
    /// interrupts, the edges between them, and which pauses are input turns.
    /// </summary>
    /// <remarks>
    /// <b>Predicates are compared as present-or-absent, not by value.</b> Two delegates cannot be
    /// compared, and the property under test is the shape rather than the conditions: a fork of the
    /// lane has to add a job, an edge or a pause somewhere, and all three are visible here.
    /// </remarks>
    private static string Topology<TState>(WorkflowDefinition<TState> definition) =>
        string.Join(
            "\n",
            (string[])
            [
                $"name={definition.Name}",
                $"entry={definition.EntryJob}",
                .. definition.Jobs
                    .OrderBy(job => job.Key, StringComparer.Ordinal)
                    .Select(job =>
                        $"job {job.Key} interrupt={job.Value.Interrupt?.ToString() ?? "none"} "
                        + $"conditional={(job.Value.InterruptWhen is not null ? "yes" : "no")}"),
                .. definition.Connections.Select(Edge<TState>),
                $"input=[{string.Join(",", definition.InputJobs.OrderBy(j => j, StringComparer.Ordinal))}]"
            ]);

    private static string Edge<TState>(Connection connection) => connection switch
    {
        DirectConnection direct => $"edge {direct.From} -> {direct.To}",
        RouterConnection<TState> router => $"edge {router.From} -> router",
        ForkConnection fork => $"edge {fork.From} -> fork[{string.Join(",", fork.Targets)}] {fork.Mode}",
        LoopConnection<TState> loop =>
            $"edge {loop.From} -> loop[{loop.LoopTarget}|{loop.ExitTarget}|{loop.MaxIterations}]",
        _ => $"edge {connection.From} -> {connection.GetType().Name}"
    };

    // ── Fixture: the shape the trip demo runs, with the answering left open ──

    private static Workflow<Trip> Wire(bool autopilot)
    {
        var store = new InMemoryPlanTreeStore();
        var executor = new PlanExecutor(store);

        var supervision = new SupervisionOptions
        {
            Store = store,
            // The one step that reports its contract cannot be met, which is what puts the question.
            // It disputes the contract it was given, not the node it is: a re-ruling replaces the
            // contract, so the second pass settles. Matching on the id instead would dispute
            // whatever replaced it too, and the run would only ever end by exhausting its budget.
            //
            // The dispute itself reaches the tree through DisputeAsync now — an external call, never
            // a node's own report (R26).
            Runner = (context, ct) =>
                context.Contract!.AcceptanceCriteria.Contains(Impossible)
                    ? Disputes(executor, context, ct)
                    : Task.FromResult(Met(context))
        };

        return new Workflow<Trip>("family-trip")
            .Supervise(
                "plan",
                PlanTree.Create(
                    "family-trip",
                    Contract("Plan a family trip", "every day fits the party's daily limit"),
                    [
                        ("day-1", Contract("Plan day 1", "day-1 fits within the party's daily limit")),
                        ("day-3", Contract("Plan day 3", "day-3 fits within the party's daily limit"))
                    ]),
                supervision,
                (state, result) => state with
                {
                    Coordination = state.Coordination is null
                        ? new PlanCoordination { Result = result, MaxChanges = 3 }
                        : state.Coordination with { Result = result, Decision = null, MaxChanges = 3 }
                })
            .Job("propose", new PlanAskingJob<Trip>(
                "propose", Offering, supervision, Read, Write, autopilot))
            .Job("choose", new AccountedSupervisorJob<Trip>(
                new PlanChoiceJob<Trip>("choose", supervision, Read, Write), Read, Write))
            .Job("author", new PlanAuthorJob<Trip>("author", Planning, supervision, Read, Write))
            .Then("plan", Workflow.Decide<Trip>(state =>
                state.Coordination?.NodeId is null ? Workflow.End : "propose"))
            .Then("propose", "choose")
            .Then("choose", Workflow.Decide<Trip>(state =>
                state.Coordination is { Decision: PlanDecision.ReplanPlan { Contract: null } } ? "author"
                : state.Coordination is { Decision: PlanDecision.AskPlan, Question: null } ? Workflow.End
                : state.Coordination is { MaxChanges: { } cap, Changes: var spent } && spent >= cap
                    ? Workflow.End
                : state.Coordination?.Question is { Outstanding: true } ? "propose"
                : state.Coordination is { Question: null, Decision: null, NodeId: not null }
                    ? Workflow.End
                : "plan"))
            .Then("author", "plan")
            .AwaitInputWhen("choose", state => state.Coordination?.Question is { Outstanding: true })
            .UseCheckpointing(new InMemoryCheckpointStore());
    }

    /// <summary>A supervisor that is a rule: one alternative, recommended, at the halt it knows.</summary>
    private static readonly PlanAdvisor Offering = (coordination, _) =>
    {
        IReadOnlyList<PlanOption> options = coordination.NodeId == "day-3"
            ?
            [
                new PlanOption
                {
                    Summary = "Split the island across two days.",
                    Recommended = true,
                    Replan = true
                }
            ]
            : [];

        return Task.FromResult(new PlanProposal { Options = options });
    };

    /// <summary>A planner that keeps day-1 as it is and rewrites day-3 into two days.</summary>
    private static readonly PlanAuthor Planning = (coordination, _, _) =>
    {
        var tree = coordination.Result.Tree;

        return Task.FromResult<AuthoredPlan?>(new AuthoredPlan
        {
            Contract = tree.Root.Contract,
            Steps =
            [
                new AuthoredStep { Id = "day-1", Contract = tree.Node("day-1").Contract },
                new AuthoredStep
                {
                    Id = "day-3",
                    Contract = Contract(
                        "Plan the island over two days", "every day fits the party's daily limit")
                }
            ]
        });
    };

    private static PlanCoordination? Read(Trip state) => state.Coordination;

    private static Trip Write(Trip state, PlanCoordination coordination) =>
        state with { Coordination = coordination };

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static NodeOutcome Met(PlanNodeContext context) => new()
    {
        Verdicts =
        [
            .. context.Contract!.AcceptanceCriteria.Select(criterion => new CriterionVerdict
            {
                Criterion = criterion, Passed = true, Oracle = "arithmetic", At = T0
            })
        ]
    };

    /// <summary>
    /// Disputes a node from outside it — a reviewer's call, never the node's own report (R26) — and
    /// returns the narration-only outcome now left for the runner to hand back.
    /// </summary>
    private static async Task<NodeOutcome> Disputes(
        PlanExecutor executor, PlanNodeContext context, CancellationToken ct)
    {
        await executor.DisputeAsync(
            context.PlanId, context.Node.Id, Impossible, "the tour does not fit between the ferries", ct)
            .ConfigureAwait(false);
        return NodeOutcome.Nothing;
    }
}
