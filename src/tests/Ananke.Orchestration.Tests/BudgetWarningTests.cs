using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// ADR-arch-028 D12: an optional warning tier that reports without stopping. A budget is a
/// guardrail against a spike, so being told before the ceiling is reached is the useful part.
/// </summary>
[TestFixture]
public class BudgetWarningTests
{
    // 1000 in + 0 out per call, at 1.0 per 1K input => 1.0 per job.
    private static BudgetConfig Rates(decimal maxCost, decimal? warnAt = null) => new()
    {
        MaxCost = maxCost,
        WarnAtCost = warnAt,
        CostPer1KInputTokens = 1.0m,
        CostPer1KOutputTokens = 0m
    };

    private static async Task<List<WorkflowEvent<BudgetState2>>> RunAsync(BudgetConfig budget, int jobs)
    {
        var model = new FixedUsageModel(inputTokens: 1000, outputTokens: 0);
        var workflow = new Workflow<BudgetState2>("budget-warn").WithBudget(budget);

        for (var i = 0; i < jobs; i++)
            workflow = workflow.Job($"job-{i}", AgentJob($"job-{i}", model));

        var chain = Enumerable.Range(0, jobs).Select(i => $"job-{i}").Append(Workflow.End).ToArray();
        workflow = workflow.Chain(chain);

        var events = new List<WorkflowEvent<BudgetState2>>();
        await foreach (var evt in workflow.StreamAsync(new BudgetState2()))
            events.Add(evt);
        return events;
    }

    [Test]
    public async Task WarnAtCost_Crossed_EmitsWarningAndKeepsRunning()
    {
        // warn at 1.5 (after job 2), limit 10 — never reached in 3 jobs.
        var events = await RunAsync(Rates(maxCost: 10m, warnAt: 1.5m), jobs: 3);

        var warning = events.OfType<BudgetWarning<BudgetState2>>().ShouldHaveSingleItem();
        warning.WarnAtCost.ShouldBe(1.5m);
        warning.Budget.ShouldBe(10m);
        warning.EstimatedCost.ShouldBeGreaterThan(1.5m);

        events.OfType<WorkflowCompleted<BudgetState2>>().ShouldNotBeEmpty(
            "a warning reports, it does not stop the run");
        events.OfType<BudgetExceeded<BudgetState2>>().ShouldBeEmpty();
    }

    [Test]
    public async Task WarnAtCost_FiresOnceEvenThoughEveryLaterJobIsAlsoOverIt()
    {
        var events = await RunAsync(Rates(maxCost: 100m, warnAt: 1.5m), jobs: 5);

        events.OfType<BudgetWarning<BudgetState2>>().Count().ShouldBe(1,
            "latched per execution — otherwise every job past the threshold spams the stream");
    }

    [Test]
    public async Task WarnAtCost_NotConfigured_EmitsNothing()
    {
        var events = await RunAsync(Rates(maxCost: 100m), jobs: 3);

        events.OfType<BudgetWarning<BudgetState2>>().ShouldBeEmpty();
    }

    [Test]
    public async Task WarnAtCost_NotReached_EmitsNothing()
    {
        var events = await RunAsync(Rates(maxCost: 100m, warnAt: 50m), jobs: 3);

        events.OfType<BudgetWarning<BudgetState2>>().ShouldBeEmpty();
    }

    [Test]
    public async Task BudgetMode_DefaultsToStop_PreservingShippedBehaviour()
    {
        new BudgetConfig { MaxCost = 1m }.Mode.ShouldBe(BudgetMode.Stop);
    }

    [Test]
    public async Task MaxCost_StillStops_WithAWarningTierConfigured()
    {
        // warn at 1.5, limit 2.5 => job 3 crosses the limit.
        var events = await RunAsync(Rates(maxCost: 2.5m, warnAt: 1.5m), jobs: 5);

        events.OfType<BudgetWarning<BudgetState2>>().ShouldHaveSingleItem();
        events.OfType<BudgetExceeded<BudgetState2>>().ShouldNotBeEmpty(
            "the warning tier must not disarm the limit");
    }

    // -- Helpers -----------------------------------------------------

    private static Jobs.IJob<BudgetState2> AgentJob(string name, IAgentModel model) =>
        AgentJobFactory.Create<BudgetState2, AgentOutput>(name, model)
            .WithPrompt(_ => "test")
            .MapResult((s, _) => s)
            .Build();

    public record BudgetState2;

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
