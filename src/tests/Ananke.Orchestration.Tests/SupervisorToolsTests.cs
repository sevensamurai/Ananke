using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Whether the role asked what a halt admits can check its own answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>A work item gets tools; the role deciding what the plan becomes got a persona.</b> It could
/// only ever offer what sounded right, and everything downstream then treated an untested guess as a
/// change of plan. That is the whole of the finding these pin.
/// </para>
/// <para>
/// <b>Wired is not called, which is the failure this iteration keeps meeting.</b> A hook that
/// existed and was never reached would look exactly like a hook that worked, so these assert what
/// the model was actually offered rather than what the supervision was configured with.
/// </para>
/// </remarks>
[TestFixture]
public class SupervisorToolsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TheAdvisor_IsOfferedTheSupervisorTools()
    {
        var model = new Recording(Proposal);

        await new AgentPlanAdvisor(WithTools(model)).ProposeAsync(Halted());

        model.Offered.ShouldNotBeNull();
        model.Offered.Select(tool => tool.Name).ShouldContain("check_day");
    }

    [Test]
    public async Task TheCoordinator_IsOfferedTheSameTools()
    {
        // The two jobs put different questions to the same evidence. One of them being the better
        // informed would be an accident of which was written first.
        var model = new Recording(Revision);

        await new AgentPlanSupervisor(WithTools(model)).DecideAsync(Halted());

        model.Offered.ShouldNotBeNull();
        model.Offered.Select(tool => tool.Name).ShouldContain("check_day");
    }

    [Test]
    public async Task WithNoToolsWired_TheRoleIsOfferedNone()
    {
        // The default stays what it was: a supervision that wires nothing gets a role that reasons
        // from the halt alone, and no empty tool list is invented for it.
        var model = new Recording(Proposal);

        await new AgentPlanAdvisor(new SupervisionOptions
        {
            Supervisor = model,
            Store = new InMemoryPlanTreeStore()
        }).ProposeAsync(Halted());

        model.Offered.ShouldBeNull();
    }

    [Test]
    public async Task TheToolsAreDescribedToTheModel_NotJustNamed()
    {
        // A tool the model cannot tell the purpose of is a tool it will not call, which is
        // indistinguishable from one that was never wired.
        var model = new Recording(Proposal);

        await new AgentPlanAdvisor(WithTools(model)).ProposeAsync(Halted());

        var scorer = model.Offered!.Single(tool => tool.Name == "check_day");

        scorer.Description.ShouldNotBeNullOrWhiteSpace();
        scorer.ParametersJsonSchema.ShouldContain("stops");
    }

    [Test]
    public async Task TheCallThatOffersTools_DoesNotAlsoDemandTheFinalSchema()
    {
        // Asked because a live supervisor with tools wired, described and offered called none of
        // them. If the same request both offers tools and demands a strict response schema, a model
        // will satisfy the schema and never reach for a tool — the hook would be present, correct,
        // and useless.
        var model = new Recording(Proposal);

        await new AgentPlanAdvisor(WithTools(model)).ProposeAsync(Halted());

        var offering = model.Requests.First(request => request.Tools is { Count: > 0 });

        offering.ResponseFormat.ShouldBeNull(
            "a call that offers tools must leave the model free to call one");
    }

    [Test]
    public async Task AnAdvisorThatRunsOutOfToolRounds_OffersNothingRatherThanKillingTheRun()
    {
        // Measured: a supervisor searching hard exhausted its rounds and the workflow ended at the
        // halt with a message about tool budgets. Offering nothing is already an outcome the tier
        // handles; faulting is an outcome about the framework rather than about the plan.
        var supervision = new SupervisionOptions
        {
            Supervisor = new Looping(),
            Store = new InMemoryPlanTreeStore(),
            SupervisorTools = new ToolKit("trip").AddTool(
                "check_day", "Score a candidate.", tool => tool
                    .Param("stops", "The stops.")
                    .OnExecute(_ => ToolResult.Ok("no"))),
            SupervisorToolRounds = 2
        };

        var proposal = await new AgentPlanAdvisor(supervision).ProposeAsync(Halted());

        proposal.Empty.ShouldBeTrue();
    }

    [Test]
    public async Task ACoordinatorThatRunsOutOfToolRounds_StopsRatherThanKillingTheRun()
    {
        var supervision = new SupervisionOptions
        {
            Supervisor = new Looping(),
            Store = new InMemoryPlanTreeStore(),
            SupervisorTools = new ToolKit("trip").AddTool(
                "check_day", "Score a candidate.", tool => tool
                    .Param("stops", "The stops.")
                    .OnExecute(_ => ToolResult.Ok("no"))),
            SupervisorToolRounds = 2
        };

        var decision = await new AgentPlanSupervisor(supervision).DecideAsync(Halted());

        decision.ShouldBeOfType<PlanDecision.AskPlan>();
    }

    /// <summary>A model that only ever asks for another tool call.</summary>
    private sealed class Looping : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(
            AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                ToolCalls = [new AgentToolCall("call-1", "check_day", """{"stops":"market"}""")]
            });
    }

    [Test]
    public async Task ToolCalls_ReachTheEventStream_WithTheirArgumentsAndResult()
    {
        var sink = new CollectingSink();
        var model = new CallsOnceThenAnswers(
            "check_day", """{"stops":"market"}""",
            """{"options":[{"summary":"Split it","replan":true,"recommended":true}]}""");

        using (WorkflowEventReporting.BeginScope(sink))
            await new AgentPlanAdvisor(WithTools(model)).ProposeAsync(Halted());

        var called = sink.Events.OfType<AgentToolCalled>().ShouldHaveSingleItem();
        called.AgentName.ShouldBe("plan-advisor");
        called.ToolName.ShouldBe("check_day");
        called.Arguments.ShouldContain("market");
        called.Result.ShouldBe("scored market");
        called.IsError.ShouldBeFalse();
    }

    /// <summary>A model that asks for one tool call, then answers.</summary>
    private sealed class CallsOnceThenAnswers(string tool, string arguments, string answer) : IAgentModel
    {
        private bool _called;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            if (!_called && request.Tools is { Count: > 0 })
            {
                _called = true;
                return Task.FromResult(new AgentResponse { ToolCalls = [new AgentToolCall("call-1", tool, arguments)] });
            }

            return Task.FromResult(new AgentResponse { Text = answer });
        }
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

    [Test]
    public async Task ADiscardedOption_SaysWhatItWasAndWhyItWentAsync()
    {
        // A discarded option is a fact about the run, not an absence of one. Silently dropped, a
        // supervisor that thought of nothing is indistinguishable from one that thought of three
        // things the wiring would not take — different problems, different fixes, and several runs
        // of hand-instrumenting the framework to tell them apart.
        var supervision = new SupervisionOptions
        {
            Supervisor = new Recording(Option(replan: false, abandon: false)),
            Store = new InMemoryPlanTreeStore()
        };

        var proposal = await new AgentPlanAdvisor(supervision).ProposeAsync(Halted());

        proposal.Empty.ShouldBeTrue();

        // Two, because a proposal that keeps nothing is asked once more with what was wrong — and
        // this model repeats itself, which is the case worth being able to see: a seat that was told
        // and corrected itself reads differently from one that was told and said the same again.
        proposal.Discarded.Count.ShouldBe(2);

        foreach (var why in proposal.Discarded)
        {
            why.ShouldContain("Split it");
            why.ShouldContain("neither gives the step up nor asks for a replan");
        }
    }

    [Test]
    public async Task AProposalThatKeptEverything_DiscardsNothing()
    {
        var supervision = new SupervisionOptions
        {
            Supervisor = new Recording(Option()),
            Store = new InMemoryPlanTreeStore()
        };

        var proposal = await new AgentPlanAdvisor(supervision).ProposeAsync(Halted());

        proposal.Options.ShouldHaveSingleItem();
        proposal.Discarded.ShouldBeEmpty();
    }

    /// <summary>An option that asks for a replan, unless told to leave both fields empty.</summary>
    private static string Option(bool replan = true, bool abandon = false) =>
        JsonSerializer.Serialize(new
        {
            options = new[]
            {
                new
                {
                    summary = "Split it",
                    replan,
                    abandon = abandon ? "not worth keeping" : null,
                    rationale = "because",
                    recommended = true
                }
            }
        });

    // ── Fixtures ──

    /// <summary>A model that answers as told, and remembers every request it was sent.</summary>
    /// <remarks>
    /// <b>Every request, not the last one.</b> A structured job runs a tool loop and then a final
    /// call that coerces the answer to the schema, and that last call carries no tools — so a fake
    /// that kept only what it saw most recently would report *no tools were offered* on a job that
    /// offered them perfectly well. It reported exactly that, before this was fixed.
    /// </remarks>
    private sealed class Recording(string reply) : IAgentModel
    {
        private readonly List<AgentRequest> _requests = [];

        public IReadOnlyList<AgentRequest> Requests => _requests;

        /// <summary>The tools offered on any call, or <see langword="null"/> if none ever were.</summary>
        public IReadOnlyList<AgentTool>? Offered =>
            _requests.Select(request => request.Tools).FirstOrDefault(tools => tools is { Count: > 0 });

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            _requests.Add(request);
            return Task.FromResult(new AgentResponse { Text = reply });
        }
    }

    private static SupervisionOptions WithTools(IAgentModel planner) => new()
    {
        Supervisor = planner,
        Store = new InMemoryPlanTreeStore(),
        SupervisorTools = new ToolKit("trip").AddTool(
            "check_day",
            "Score a candidate day without recording it.",
            tool => tool
                .Param("stops", "The stop ids in visiting order, comma separated.")
                .OnExecute(args => ToolResult.Ok($"scored {args.Get("stops")}")))
    };

    private static readonly string Proposal = JsonSerializer.Serialize(new
    {
        options = new[]
        {
            new
            {
                summary = "Split it",
                goal = "Split it",
                criteria = new[] { "it works" },
                rationale = "because",
                recommended = true
            }
        }
    });

    private static readonly string Revision = JsonSerializer.Serialize(new
    {
        goal = "Split it",
        criteria = new[] { "it works" },
        rationale = "because"
    });

    private static PlanCoordination Halted()
    {
        var tree = PlanTree.Create(
                "plan",
                new AgentContract { Goal = "Plan a trip", AcceptanceCriteria = ["every day fits"] },
                [("day-3", new AgentContract
                {
                    Goal = "Plan day 3",
                    AcceptanceCriteria = ["day-3 fits within the party's daily limit"]
                })])
            .WithViolation("day-3", new PlanViolation
            {
                Criterion = "day-3 fits within the party's daily limit",
                Reason = "the tour cannot be done between the ferries",
                At = T0
            });

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["day-3"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "day-3"
            }
        };
    }
}
