using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The shipped coordinator: a model deciding what a halted plan should become.
/// </summary>
/// <remarks>
/// <para>
/// Most of what is asserted here is what the model is <em>not</em> allowed to do. It authors a goal
/// and criteria; it does not get to drop a constraint, because dropping the rule a node hit resolves
/// every dispute and delivers nothing, and it does not get to mint a contract with no criteria,
/// because such a plan could never be shown to have worked.
/// </para>
/// <para>
/// The rest is about evidence. A planner shown only *"this contract is disputed"* is being asked to
/// explain a failure it did not witness, and a live model did exactly that — inventing a tool
/// limitation that does not exist. So what reaches the prompt is pinned.
/// </para>
/// </remarks>
[TestFixture]
public class AgentPlanCoordinatorTests
{
    private const string Constraint = "No new external service dependencies.";
    private const string Disputed = "The report is assembled in one buffer.";

    [Test]
    public async Task DecideAsync_ADisputedNode_ReRulesWithWhatThePlannerAuthored()
    {
        var decision = await Coordinator(Proposes("Stream it", ["Memory stays flat."], "4 GB report"))
            .DecideAsync(Halt());

        var rerule = decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
        rerule.Contract!.Goal.ShouldBe("Stream it");
        rerule.Contract.AcceptanceCriteria.ShouldBe(["Memory stays flat."]);

        // What the model wrote is its rationale, attributed to the role — never the version's reason,
        // which was observed at the halt and which the model did not witness.
        rerule.Rationale.ShouldNotBeNull();
        rerule.Rationale.By.ShouldBe(PlanRoles.Supervisor);
        rerule.Rationale.Text.ShouldBe("4 GB report");
    }

    [Test]
    public async Task DecideAsync_AReplacementContract_KeepsTheConstraintsTheLevelAboveSet()
    {
        // The whole failure mode this guards: a plan that answers "this cannot be done under that
        // rule" by deleting the rule, and then reports success.
        var decision = await Coordinator(
                Proposes("Stream it", ["Memory stays flat."], "4 GB", constraints: ["Anything goes."]))
            .DecideAsync(Halt());

        var rerule = decision.ShouldBeOfType<PlanDecision.ReplanPlan>();
        rerule.Contract!.Constraints.ShouldBe([Constraint]);
    }

    [Test]
    public async Task DecideAsync_ASupervisorWithNothingToPropose_AsksRatherThanMintingAnEmptyContract()
    {
        var decision = await Coordinator(Proposes("Give up", [], "no idea")).DecideAsync(Halt());

        decision.ShouldBeOfType<PlanDecision.AskPlan>().Options.ShouldBeEmpty();
    }

    [Test]
    public async Task DecideAsync_ThePrompt_ShowsTheDisputeTheVerdictsAndThePlan()
    {
        string? prompt = null;
        var model = new SimulatedAgentModel(request =>
        {
            prompt = string.Join('\n', request.Messages.Select(m => m.Content));
            return Json("Stream it", ["Memory stays flat."], "4 GB");
        });

        await new AgentPlanSupervisor(new SupervisionOptions { Supervisor = model }).DecideAsync(Halt());

        var text = prompt.ShouldNotBeNull();

        text.ShouldContain(Disputed);                          // the criterion it would not route around
        text.ShouldContain("no buffer size is large enough");   // the witness's own reason, verbatim
        text.ShouldContain(Constraint);                         // the rule it may not drop
        text.ShouldContain("not met");                          // what the node decided, not only that it complained
        text.ShouldContain("Ship offline export.");             // the plan around it, not just the step that stopped
    }

    [Test]
    public async Task DecideAsync_ThePrompt_ShowsTheRecordedReasonAndDoesNotAskForOne()
    {
        // I4, at its source. A required "why did this go wrong" field, handed to a model that did
        // not witness it, is answered — and a live planner answered it by inventing a tool
        // limitation that does not exist. The reason is already recorded, so the prompt shows it and
        // asks for something the model is actually in a position to know.
        string? prompt = null;
        var model = new SimulatedAgentModel(request =>
        {
            prompt = string.Join('\n', request.Messages.Select(m => m.Content));
            return Json("Stream it", ["Memory stays flat."], "4 GB");
        });

        await new AgentPlanSupervisor(new SupervisionOptions { Supervisor = model }).DecideAsync(Halt());

        var text = prompt.ShouldNotBeNull();

        text.ShouldContain("already stands recorded");
        text.ShouldContain("no buffer size is large enough");   // the recorded reason itself
        text.ShouldContain("Do not restate what went wrong");
        text.ShouldContain("rationale");
    }

    [Test]
    public async Task DecideAsync_ThePrompt_NeverShowsTheNodesOwnAccountOfItself()
    {
        // A node's narration of its own work is not evidence, and E2 took it out of this prompt
        // entirely — it still reaches the event stream, and reaches no decision beyond that. The
        // fixture's own account text must not be there, whatever it said.
        string? prompt = null;
        var model = new SimulatedAgentModel(request =>
        {
            prompt = string.Join('\n', request.Messages.Select(m => m.Content));
            return Json("Stream it", ["Memory stays flat."], "4 GB");
        });

        await new AgentPlanSupervisor(new SupervisionOptions { Supervisor = model }).DecideAsync(Halt());

        prompt.ShouldNotBeNull().ShouldNotContain(Account);
    }

    [Test]
    public async Task DecideAsync_NoSupervisorModel_SaysWhichRoleIsMissing()
    {
        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => new AgentPlanSupervisor(new SupervisionOptions()).DecideAsync(Halt()));

        thrown.Message.ShouldContain("supervisor");
    }

    [Test]
    public async Task DecideAsync_TheSupervisorRole_IsReadFromTheSupervisionRatherThanTheExecutor()
    {
        // The escalation is the point: the model that re-authors a plan is not the one that failed
        // to satisfy it.
        var supervision = new SupervisionOptions
        {
            Executor = Proposes("the worker answered", ["wrong model"], "wrong"),
            Supervisor = Proposes("the planner answered", ["right model"], "right")
        };

        var decision = await new AgentPlanSupervisor(supervision).DecideAsync(Halt());

        decision.ShouldBeOfType<PlanDecision.ReplanPlan>().Contract!.Goal.ShouldBe("the planner answered");
    }

    // ── Fixtures ──

    private static AgentPlanSupervisor Coordinator(IAgentModel planner) =>
        new(new SupervisionOptions { Supervisor = planner });

    private static IAgentModel Proposes(
        string goal, string[] criteria, string rationale, string[]? constraints = null) =>
        SimulatedAgentModel.Fixed(Json(goal, criteria, rationale, constraints));

    /// <remarks>
    /// The shape the planner answers in, and it has <b>no reason field</b>. A model asked why the
    /// old contract stopped being right would be explaining something it did not witness; what it is
    /// asked for is its argument for the replacement it just wrote.
    /// </remarks>
    private static string Json(
        string goal, string[] criteria, string rationale, string[]? constraints = null) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            goal,
            criteria,
            rationale,
            constraints = constraints ?? []
        });

    private const string Account =
        "I sized the largest tenant's report at 4 GB and the export host at 2 GB, then tried three "
        + "buffer sizes.";

    private static PlanCoordination Halt(string? summary = Account)
    {
        var tree = PlanTree.Create(
            "offline-export",
            new AgentContract { Goal = "Ship offline export.", AcceptanceCriteria = ["It exports."] },
            [
                ("build-buffer", new AgentContract
                {
                    Goal = "Assemble the report in memory.",
                    AcceptanceCriteria = [Disputed],
                    Constraints = [Constraint]
                })
            ]);

        tree = tree
            .WithVerdict("build-buffer", new CriterionVerdict
            {
                Criterion = Disputed,
                Passed = false,
                Oracle = "dotnet test",
                At = DateTimeOffset.UnixEpoch
            })
            .WithViolation("build-buffer", new PlanViolation
            {
                Criterion = Disputed,
                Reason = "The largest tenant's report is 4 GB; no buffer size is large enough.",
                At = DateTimeOffset.UnixEpoch
            })
            .WithReading("build-buffer", new NodeReading
            {
                At = DateTimeOffset.UnixEpoch,
                PlanVersion = 1,
                ProjectedTokens = 40,
                Summary = summary
            });

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["build-buffer"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "build-buffer"
            },
            MaxChanges = 3
        };
    }
}
