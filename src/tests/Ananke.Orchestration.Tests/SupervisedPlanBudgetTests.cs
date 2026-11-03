using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Patterns;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A budget on a supervised-plan workflow, against the model calls every role makes inside the
/// plan: the Executor running a step, the Supervisor recommending an option, and a Planner that
/// rewrites the plan through its own agent job.
/// </summary>
[TestFixture]
public class SupervisedPlanBudgetTests
{
    // Each role's call costs a different number of input tokens, so the total says whose calls
    // were counted.
    private const int ExecutorTokens = 1;
    private const int SupervisorTokens = 1_000;
    private const int PlannerTokens = 1_000_000;

    private sealed record TripState
    {
        public PlanCoordination? Coordination { get; init; }
    }

    [Test]
    public async Task SupervisedPlan_EveryRolesModelCalls_CountTowardTheRunsUsage()
    {
        var roles = new Roles();

        var result = await roles.Workflow(BudgetConfig.FromPerMillion(
                maxCost: 1_000m, inputPerMillion: 1m, outputPerMillion: 0m))
            .RunAsync(new TripState()).WaitAsync(TimeSpan.FromSeconds(10));

        roles.Executor.Calls.ShouldBeGreaterThan(0);
        roles.Supervisor.Calls.ShouldBeGreaterThan(0);
        roles.Planner.Calls.ShouldBeGreaterThan(0);
        result.CumulativeUsage.InputTokens.ShouldBe(
            roles.Executor.Calls * ExecutorTokens
            + roles.Supervisor.Calls * SupervisorTokens
            + roles.Planner.Calls * PlannerTokens);
    }

    [Test]
    public async Task SupervisedPlan_APlannerCallOverTheBudget_EndsTheRunAsBudgetExceeded()
    {
        // One Planner call costs 1.00 at these rates, twice the ceiling.
        var roles = new Roles();

        var result = await roles.Workflow(BudgetConfig.FromPerMillion(
                maxCost: 0.50m, inputPerMillion: 1m, outputPerMillion: 0m))
            .RunAsync(new TripState()).WaitAsync(TimeSpan.FromSeconds(10));

        result.Status.ShouldBe(ExecutionStatus.BudgetExceeded);
        roles.Planner.Calls.ShouldBe(1);
        result.EstimatedCost.ShouldBeGreaterThanOrEqualTo(1m);
    }

    /// <summary>The three models, and a workflow that reaches each of them once a step cannot be done.</summary>
    private sealed class Roles
    {
        public SimulatedAgentModel Executor { get; } = SimulatedAgentModel.Sequence(
            [
                Json(new PlanNodeReport
                {
                    Summary = "the stay cannot be had on those dates",
                    Done = false,
                    Options = ["stay one night fewer", "move the stay a day later"]
                }),
                Json(new PlanNodeReport { Summary = "booked", Done = true })
            ],
            Tokens(ExecutorTokens));

        public SimulatedAgentModel Supervisor { get; } = SimulatedAgentModel.Json(
            new { option = 1, why = "the dates matter more than the length" },
            Tokens(SupervisorTokens));

        public SimulatedAgentModel Planner { get; } = SimulatedAgentModel.Json(
            new AuthoredPlan
            {
                Contract = Contract("A week away", "the trip is booked"),
                Steps = [new AuthoredStep { Id = "trip-shorter", Contract = Contract("Book the shorter stay", "the shorter stay is booked") }]
            },
            Tokens(PlannerTokens));

        public Workflow<TripState> Workflow(BudgetConfig budget)
        {
            var supervision = new SupervisionOptions
            {
                Store = new InMemoryPlanTreeStore(),
                Verifier = new DeterministicVerifier(
                [
                    new PredicateCheck("the first stay", ["the stay is booked"], _ => false),
                    new PredicateCheck("the shorter stay", ["the shorter stay is booked"], _ => true)
                ]),
                Executor = Executor,
                Supervisor = Supervisor
            };

            return AgenticPattern.SupervisedPlan<TripState>("trip")
                .WithPlan(PlanTree.Create(
                    "trip",
                    Contract("A week away", "the trip is booked"),
                    [("trip-stay", Contract("Book the stay", "the stay is booked"))]))
                .Supervised(supervision)
                .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
                .WithAdvisor(new AgentPlanAdvisor(supervision).AsAdvisor(), unattended: true)
                .WithPlanner(AuthorAsync)
                .MaxChangesOfPlan(1)
                .Build()
                .WithBudget(budget);
        }

        private async Task<AuthoredPlan?> AuthorAsync(
            PlanCoordination coordination, IReadOnlyList<string> refused, CancellationToken ct)
        {
            AuthoredPlan? authored = null;

            var job = AgentJobFactory.Create<PlanCoordination, AuthoredPlan>("planner", Planner)
                .WithPrompt(_ => "Rewrite the plan.")
                .MapResult((halt, produced) =>
                {
                    authored = produced;
                    return halt;
                })
                .Build();

            await job.ExecuteAsync(coordination, ct).ConfigureAwait(false);
            return authored;
        }
    }

    private static SimulatedModelOptions Tokens(int input) => new() { InputTokens = input };

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };
}
