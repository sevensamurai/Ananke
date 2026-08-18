using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// ADR-arch-028 D11/D13: the budget must bind inside fork branches, not only on the main path.
/// It matters most since D4 made loops work in branches — a cycle inside one would otherwise
/// spend with no check ever running, which is precisely the runaway a guardrail exists for.
/// </summary>
[TestFixture]
public class BranchBudgetStopTests
{
    // 1000 input tokens per call at 1.0 per 1K => 1.0 per job.
    private static BudgetConfig Rates(decimal maxCost) => new()
    {
        MaxCost = maxCost,
        CostPer1KInputTokens = 1.0m,
        CostPer1KOutputTokens = 0m
    };

    [Test]
    public async Task LoopingBranch_StopsAtTheBudget_InsteadOfSpendingUnbounded()
    {
        var model = new FixedUsageModel(1000, 0);

        // Without a budget this branch would run 50 iterations at 1.0 each.
        var execution = await new Workflow<BudgetLoopState>("branch-runaway")
            .WithBudget(Rates(maxCost: 3m))
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("spender", AgentJob("spender", model))
            .Job("branch-b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("spender", "branch-b"))
            .Loop("spender", loopTarget: "spender", exitTarget: "merge",
                  until: _ => false, maxIterations: 50)
            .Join(["merge", "branch-b"], "merge", states => states[0])
            .RunAsync(new BudgetLoopState());

        execution.Status.ShouldBe(ExecutionStatus.BudgetExceeded,
            "a looping branch must be stopped by the budget, not run to its iteration cap");
        execution.EstimatedCost.ShouldBeLessThan(50m,
            "stopping early is the entire point — 50 iterations would be the unguarded cost");
    }

    [Test]
    public async Task StoppedBranch_IsReportedAsStopped_NotFaultedOrCancelled()
    {
        var model = new FixedUsageModel(1000, 0);

        var execution = await new Workflow<BudgetLoopState>("branch-outcome")
            .WithBudget(Rates(maxCost: 3m))
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("spender", AgentJob("spender", model))
            .Job("branch-b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("spender", "branch-b"))
            .Loop("spender", loopTarget: "spender", exitTarget: "merge",
                  until: _ => false, maxIterations: 50)
            .Join(["merge", "branch-b"], "merge", states => states[0])
            .RunAsync(new BudgetLoopState());

        var outcomes = execution.Result!.BranchOutcomes;
        outcomes.ShouldNotBeEmpty("a stopped branch is a fact the caller should see");

        var stopped = outcomes.Where(o => o.Kind == BranchOutcomeKind.Stopped).ToList();
        stopped.ShouldNotBeEmpty();
        outcomes.ShouldAllBe(o => o.Kind != BranchOutcomeKind.Faulted,
            "nothing went wrong — the branch was ended by policy");
    }

    [Test]
    public async Task BranchesWithinBudget_StillJoinNormally()
    {
        var model = new FixedUsageModel(100, 0);   // 0.1 per job

        var execution = await new Workflow<BudgetLoopState>("branch-within")
            .WithBudget(Rates(maxCost: 100m))
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("branch-a", AgentJob("branch-a", model))
            .Job("branch-b", AgentJob("branch-b", model))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("branch-a", "branch-b"))
            .Join(["branch-a", "branch-b"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new BudgetLoopState());

        execution.Status.ShouldBe(ExecutionStatus.Completed,
            "the branch-side check must not disturb a fork that stays within budget");
        execution.Result!.BranchOutcomes
            .ShouldAllBe(o => o.Kind == BranchOutcomeKind.Succeeded);
    }

    // -- Helpers -----------------------------------------------------

    private static Jobs.IJob<BudgetLoopState> AgentJob(string name, IAgentModel model) =>
        AgentJobFactory.Create<BudgetLoopState, AgentOutput>(name, model)
            .WithPrompt(_ => "test")
            .MapResult((s, _) => s)
            .Build();

    public record BudgetLoopState;

    private record AgentOutput;

    private sealed class FixedUsageModel(int inputTokens, int outputTokens) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                Text = "{}",
                Usage = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens }
            });
    }
}
