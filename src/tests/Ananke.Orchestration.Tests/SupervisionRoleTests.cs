using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Whether the roles a supervision names are actually filled by what it names.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because every gate was green while one of them was not.</b> The plan tier reserves
/// re-planning for a stronger model than the one doing the work — that is the reason `Planner` and
/// `Executor` are separate roles at all. Across two demos and a dozen live runs, the two resolved to
/// the same model every time, and nothing anywhere said so: not a test, not an event, not the trace.
/// </para>
/// <para>
/// These pin what the tier is responsible for: routing each role to the model it was given, and
/// refusing rather than substituting when one is missing. The last test records what the tier is
/// deliberately <em>not</em> responsible for — noticing that two roles hold one model, which is a
/// wiring fact and belongs to whoever did the wiring.
/// </para>
/// </remarks>
[TestFixture]
public class SupervisionRoleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TheAdvisor_AsksTheSupervisorModel_NotTheExecutors()
    {
        // Distinguishable by what each would say, because that is the only handle there is: a model
        // has no identity, so which one answered can only be inferred from the answer.
        var supervision = Roles(
            executor: Says("the executor answered", "Executor's idea"),
            planner: Says("the planner answered", "Planner's idea"));

        var proposal = await new AgentPlanAdvisor(supervision).ProposeAsync(Halted());

        proposal.Options.ShouldHaveSingleItem().Summary.ShouldBe("Planner's idea");
    }

    [Test]
    public async Task TheCoordinator_AsksTheSupervisorModel_NotTheExecutors()
    {
        var supervision = Roles(
            executor: Revision("Executor's replacement"),
            planner: Revision("Planner's replacement"));

        var decision = await new AgentPlanSupervisor(supervision).DecideAsync(Halted());

        decision.ShouldBeOfType<PlanDecision.ReplanPlan>()
            .Contract!.Goal.ShouldBe("Planner's replacement");
    }

    [Test]
    public void ASupervisionWithNoSupervisor_RefusesRatherThanQuietlyUsingTheExecutor()
    {
        // The failure that would hide the defect completely. If a missing supervisor fell back to
        // the worker, a run with one model configured would look exactly like a run with two — and
        // "the stronger model re-planned" would be false with nothing to notice it.
        var supervision = new SupervisionOptions
        {
            Executor = Says("only the executor", "Executor's idea"),
            Store = new InMemoryPlanTreeStore()
        };

        Should.Throw<InvalidOperationException>(
                () => new AgentPlanAdvisor(supervision).ProposeAsync(Halted()))
            .Message.ShouldContain("supervisor");
    }

    [Test]
    public async Task TwoRolesHeldByOneModel_AreIndistinguishableToEverythingBelowTheCaller()
    {
        // ── Characterisation, not approval ──────────────────────────────────────────────────────
        // This records the gap that let the defect live for a dozen runs: `IAgentModel` has no
        // identity, so a supervision cannot tell whether its two roles hold the same model, and
        // nothing it produces carries which model decided. A caller that wired one model into both
        // roles gets a run that is indistinguishable from a correctly escalated one.
        //
        // The demos knew only because they kept the id strings they had constructed the models
        // from. Nothing below them could.
        //
        // **This blindness is deliberate and stays.** The tier reasons about roles, not about model
        // names, and giving a model an identity so the tier could be suspicious of its own
        // configuration would answer the question in the wrong place. The question is answerable
        // where it arises — at wire-up, by whoever chose both models and knows both ids — and that
        // is where the assertion belongs. This test exists so the blindness is a decision rather
        // than a surprise.
        var shared = Says("one model for both", "The only idea");

        var supervision = Roles(executor: shared, planner: shared);

        var proposal = await new AgentPlanAdvisor(supervision).ProposeAsync(Halted());

        proposal.Options.ShouldHaveSingleItem();

        // The supervision holds the same instance in both roles, and offers no way to observe it —
        // there is no `Escalates`, no role report, and no model name to compare.
        supervision.ModelFor(PlanRoles.Executor)
            .ShouldBeSameAs(supervision.ModelFor(PlanRoles.Supervisor));

        // And the decision the tier reports says what was decided, never by what.
        typeof(PlanDecisionTaken).GetProperties()
            .Select(p => p.Name)
            .ShouldNotContain(
                name => name.Contains("Model", StringComparison.OrdinalIgnoreCase),
                "PlanDecisionTaken records no model, so an audit cannot say which one re-planned");
    }

    // ── Fixtures ──

    private static SupervisionOptions Roles(IAgentModel executor, IAgentModel planner) =>
        new()
        {
            Executor = executor,
            Supervisor = planner,
            Store = new InMemoryPlanTreeStore()
        };

    /// <summary>A model that offers exactly one alternative, labelled so the answer names its author.</summary>
    private static IAgentModel Says(string rationale, string summary) =>
        SimulatedAgentModel.Fixed(JsonSerializer.Serialize(new
        {
            options = new[]
            {
                new
                {
                    summary,
                    replan = true,
                    rationale,
                    recommended = true
                }
            }
        }));

    /// <summary>A model that answers the coordinator's question instead of the advisor's.</summary>
    private static IAgentModel Revision(string goal) =>
        SimulatedAgentModel.Fixed(JsonSerializer.Serialize(new
        {
            goal,
            criteria = new[] { "it works" },
            rationale = "because"
        }));

    private static PlanCoordination Halted()
    {
        var tree = PlanTree.Create(
                "plan",
                new AgentContract { Goal = "Ship it", AcceptanceCriteria = ["it ships"] },
                [("parse", new AgentContract
                {
                    Goal = "Parse the input",
                    AcceptanceCriteria = ["the parser round-trips"]
                })])
            .WithViolation("parse", new PlanViolation
            {
                Criterion = "the parser round-trips",
                Reason = "the input has two dialects and the contract names one",
                At = T0
            });

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["parse"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "parse"
            }
        };
    }
}
