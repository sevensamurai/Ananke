using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Patterns;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A budget over calls priced by a cost-resolving router, as a run records them: the agent job
/// reports the call's tokens, and the router reports the call's cost.
/// </summary>
[TestFixture]
public class RoutedCostBudgetTests
{
    private sealed record State
    {
        public string Text { get; init; } = "";

        public PlanCoordination? Coordination { get; init; }
    }

    private sealed record Answer
    {
        public string? Text { get; init; }
    }

    [Test]
    public async Task WithBudget_ARoutedCallBesideFlatRates_IsChargedItsRoutedCostOnce()
    {
        // A million input tokens at the router's $2 per million is 2.00. The flat rate is for
        // calls that arrive with no cost of their own; a routed call is not one of them.
        var routed = Priced(SimulatedAgentModel.Json(new Answer { Text = "hi" },
            new SimulatedModelOptions { InputTokens = 1_000_000 }), inputPerMillion: 2m);

        var result = await new Workflow<State>("routed")
            .Job("ask", AgentJobFactory.Create<State, Answer>("ask", routed)
                .WithPrompt(_ => "say hi")
                .MapResult((s, r) => s with { Text = r.Text ?? "" })
                .Build())
            .Then("ask", Workflow.End)
            .WithBudget(BudgetConfig.FromPerMillion(maxCost: 100m, inputPerMillion: 1m, outputPerMillion: 0m))
            .RunAsync(new State())
            .WaitAsync(TimeSpan.FromSeconds(10));

        result.EstimatedCost.ShouldBe(2.00m);
    }

    [Test]
    public void Build_ASupervisedPlanWhoseRolesAreRouted_AcceptsABudgetWithNoFlatRates()
    {
        // Every role's calls report their own cost, so there is nothing for a flat rate to price.
        var supervision = new SupervisionOptions
        {
            Executor = Priced(SimulatedAgentModel.Json(new PlanNodeReport { Summary = "done", Done = true }), 1m),
            Supervisor = Priced(SimulatedAgentModel.Json(new { option = 1 }), 1m)
        };

        var workflow = AgenticPattern.SupervisedPlan<State>("plan")
            .WithPlan(PlanTree.Create("plan", Contract("Ship it", "it ships"), [("plan-step", Contract("Step", "it steps"))]))
            .Supervised(supervision)
            .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
            .WithAdvisor(new AgentPlanAdvisor(supervision).AsAdvisor(), unattended: true)
            .Build()
            .WithBudget(new BudgetConfig { MaxCost = 1m });

        Should.NotThrow(() => workflow.Build());
    }

    private static IAgentModel Priced(IAgentModel model, decimal inputPerMillion) =>
        new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "priced",
                Model = model,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput
                    | ModelCapability.ToolCalling,
                CostPer1KInputTokens = inputPerMillion / 1000m
            })
            .ToAgentModel();

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };
}
