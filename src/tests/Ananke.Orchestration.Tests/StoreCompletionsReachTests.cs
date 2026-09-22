using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Whether a run actually reaches a provider's platform logs.
/// </summary>
/// <remarks>
/// <c>StoredOutputEnabled</c> is a property of each request, not a batch a client flushes — so if a
/// provider's log page is empty after a run, the flag did not reach the request. The chain is long
/// enough to be worth pinning: the workflow holds it, the runner puts it in an <c>AsyncLocal</c>
/// trace context, and the agent engine reads it back several jobs deep, inside a supervised plan.
/// </remarks>
[TestFixture]
public class StoreCompletionsReachTests
{
    private sealed record DeliveryState
    {
        public PlanCoordination? Coordination { get; init; }
    }

    [Test]
    public async Task StreamAsync_UnderAPatternWithStoringOn_EveryPlanNodeRequestCarriesIt()
    {
        var seen = new List<bool>();
        var workflow = Delivery(seen, storing: true);

        await foreach (var _ in workflow.StreamAsync(new DeliveryState())) { }

        seen.ShouldNotBeEmpty();
        seen.ShouldAllBe(x => x);
    }

    [Test]
    public async Task StreamAsync_WithStoringOff_NoRequestCarriesIt()
    {
        // The default, and the reason a provider's log page is empty until someone asks.
        var seen = new List<bool>();
        var workflow = Delivery(seen, storing: false);

        await foreach (var _ in workflow.StreamAsync(new DeliveryState())) { }

        seen.ShouldNotBeEmpty();
        seen.ShouldAllBe(x => !x);
    }

    private static Workflow<DeliveryState> Delivery(List<bool> seen, bool storing)
    {
        var model = new SimulatedAgentModel(request =>
        {
            seen.Add(request.StoreCompletions);
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                summary = "did it",
                criteria = new[] { new { criterion = "it parses", met = true, evidence = "ran it" } }
            });
        });

        var workflow = Ananke.Orchestration.AgenticPattern.SupervisedPlan<DeliveryState>("delivery")
            .WithPlan(PlanTree.Create(
                "plan",
                new AgentContract { Goal = "Ship", AcceptanceCriteria = ["it parses"] },
                [("parse", new AgentContract { Goal = "Parse", AcceptanceCriteria = ["it parses"] })]))
            .Supervised(new SupervisionOptions { Executor = model })
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithCoordinator((_, _) => Task.FromResult(PlanDecision.Ask([])))
            .Build();

        return storing ? workflow.StoreCompletions(true) : workflow;
    }
}
