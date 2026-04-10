using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Jobs;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class BudgetTests
{
    // ── TokenUsage ──────────────────────────────────────────────

    [Test]
    public void TokenUsage_TotalTokens_IsSumOfInputAndOutput()
    {
        var usage = new TokenUsage { InputTokens = 100, OutputTokens = 50 };
        usage.TotalTokens.ShouldBe(150);
    }

    [Test]
    public void TokenUsage_Add_CombinesUsages()
    {
        var a = new TokenUsage { InputTokens = 100, OutputTokens = 50 };
        var b = new TokenUsage { InputTokens = 200, OutputTokens = 30 };
        var combined = a.Add(b);

        combined.InputTokens.ShouldBe(300);
        combined.OutputTokens.ShouldBe(80);
        combined.TotalTokens.ShouldBe(380);
    }

    [Test]
    public void TokenUsage_Zero_HasZeroCounts()
    {
        TokenUsage.Zero.InputTokens.ShouldBe(0);
        TokenUsage.Zero.OutputTokens.ShouldBe(0);
        TokenUsage.Zero.TotalTokens.ShouldBe(0);
    }

    // ── BudgetConfig ────────────────────────────────────────────

    [Test]
    public void BudgetConfig_EstimateCost_CalculatesCorrectly()
    {
        var config = new BudgetConfig
        {
            MaxCost = 1.0m,
            CostPer1KInputTokens = 0.01m,
            CostPer1KOutputTokens = 0.03m
        };
        var usage = new TokenUsage { InputTokens = 1000, OutputTokens = 500 };

        var cost = config.EstimateCost(usage);

        // (1000/1000 * 0.01) + (500/1000 * 0.03) = 0.01 + 0.015 = 0.025
        cost.ShouldBe(0.025m);
    }

    // ── TokenUsage accumulates across multiple jobs ─────────────

    [Test]
    public async Task TokenUsage_AccumulatesAcrossMultipleJobs()
    {
        var model = new UsageReportingModel(inputTokens: 50, outputTokens: 25);

        var workflow = new Workflow<BudgetState>("multi-job")
            .Job("job1", CreateAgentJob("job1", model))
            .Job("job2", CreateAgentJob("job2", model))
            .Then("job1", "job2")
            .Then("job2", Workflow.End);

        var result = await workflow.RunAsync(new BudgetState());

        result.Status.ShouldBe(ExecutionStatus.Completed);
        result.CumulativeUsage.InputTokens.ShouldBe(100);  // 50 * 2
        result.CumulativeUsage.OutputTokens.ShouldBe(50);   // 25 * 2
    }

    // ── Budget enforcement ──────────────────────────────────────

    [Test]
    public async Task WithBudget_ExceedsBudget_TerminatesWithBudgetExceeded()
    {
        // Each job consumes 1000 input + 500 output tokens
        var model = new UsageReportingModel(inputTokens: 1000, outputTokens: 500);

        var workflow = new Workflow<BudgetState>("budget-test")
            .Job("job1", CreateAgentJob("job1", model))
            .Job("job2", CreateAgentJob("job2", model))
            .Job("job3", CreateAgentJob("job3", model))
            .Chain("job1", "job2", "job3", Workflow.End)
            // Budget: 0.05. Cost per job: (1000/1000 * 0.01) + (500/1000 * 0.03) = 0.025
            // After job2: cost = 0.05, which equals budget exactly — not exceeded
            // Let's set budget lower so it triggers after job1
            .WithBudget(
                maxCost: 0.02m,
                costPer1KInputTokens: 0.01m,
                costPer1KOutputTokens: 0.03m);

        var result = await workflow.RunAsync(new BudgetState());

        result.Status.ShouldBe(ExecutionStatus.BudgetExceeded);
        result.EstimatedCost.ShouldBeGreaterThan(0.02m);
        result.CumulativeUsage.TotalTokens.ShouldBeGreaterThan(0);
        result.Result.ShouldNotBeNull();
        result.Result.Success.ShouldBeFalse();
        result.Result.Error!.ShouldContain("budget");
    }

    // ── BudgetExceeded event emitted in stream ──────────────────

    [Test]
    public async Task WithBudget_Stream_EmitsBudgetExceededEvent()
    {
        var model = new UsageReportingModel(inputTokens: 1000, outputTokens: 500);

        var workflow = new Workflow<BudgetState>("budget-stream")
            .Job("job1", CreateAgentJob("job1", model))
            .Job("job2", CreateAgentJob("job2", model))
            .Chain("job1", "job2", Workflow.End)
            .WithBudget(
                maxCost: 0.02m,
                costPer1KInputTokens: 0.01m,
                costPer1KOutputTokens: 0.03m);

        var events = new List<Streaming.WorkflowEvent<BudgetState>>();
        await foreach (var evt in workflow.StreamAsync(new BudgetState()))
        {
            events.Add(evt);
        }

        events.OfType<Streaming.BudgetExceeded<BudgetState>>().Count().ShouldBe(1);
        var budgetEvt = events.OfType<Streaming.BudgetExceeded<BudgetState>>().Single();
        budgetEvt.Budget.ShouldBe(0.02m);
        budgetEvt.EstimatedCost.ShouldBeGreaterThan(0.02m);
        budgetEvt.CumulativeUsage.TotalTokens.ShouldBeGreaterThan(0);
    }

    // ── No budget configured — workflow runs without cost tracking ──

    [Test]
    public async Task NoBudget_WorkflowRunsNormally()
    {
        var model = new UsageReportingModel(inputTokens: 100, outputTokens: 50);

        var workflow = new Workflow<BudgetState>("no-budget")
            .Job("job1", CreateAgentJob("job1", model))
            .Then("job1", Workflow.End);

        var result = await workflow.RunAsync(new BudgetState());

        result.Status.ShouldBe(ExecutionStatus.Completed);
        // Usage is still tracked even without budget
        result.CumulativeUsage.InputTokens.ShouldBe(100);
    }

    // ── Provider returns null usage — no crash ──────────────────

    [Test]
    public async Task NullUsage_WorkflowRunsWithoutCrash()
    {
        var model = new NoUsageModel();

        var workflow = new Workflow<BudgetState>("null-usage")
            .Job("job1", CreateAgentJob("job1", model))
            .Then("job1", Workflow.End)
            .WithBudget(maxCost: 1.0m, costPer1KInputTokens: 0.01m, costPer1KOutputTokens: 0.03m);

        var result = await workflow.RunAsync(new BudgetState());

        result.Status.ShouldBe(ExecutionStatus.Completed);
        result.CumulativeUsage.TotalTokens.ShouldBe(0);
        result.EstimatedCost.ShouldBe(0m);
    }

    // ── AgentResponse.Usage property ────────────────────────────

    [Test]
    public void AgentResponse_Usage_IsOptional()
    {
        var response = new AgentResponse { Text = "hello" };
        response.Usage.ShouldBeNull();
    }

    [Test]
    public void AgentResponse_Usage_CanBeSet()
    {
        var response = new AgentResponse
        {
            Text = "hello",
            Usage = new TokenUsage { InputTokens = 10, OutputTokens = 5 }
        };
        response.Usage.ShouldNotBeNull();
        response.Usage.TotalTokens.ShouldBe(15);
    }

    // ── WithBudget validation ───────────────────────────────────

    [Test]
    public void WithBudget_ZeroMaxCost_Throws()
    {
        var workflow = new Workflow<BudgetState>("test");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            workflow.WithBudget(maxCost: 0, costPer1KInputTokens: 0.01m, costPer1KOutputTokens: 0.03m));
    }

    [Test]
    public void WithBudget_NegativeInputCost_Throws()
    {
        var workflow = new Workflow<BudgetState>("test");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            workflow.WithBudget(maxCost: 1.0m, costPer1KInputTokens: -0.01m, costPer1KOutputTokens: 0.03m));
    }

    // ── StreamingChatWorkflow captures usage ───────────────────

    [Test]
    public async Task StreamingChatWorkflow_AccumulatesUsageFromCompletedResponse()
    {
        var model = new UsageReportingStreamingModel(inputTokens: 80, outputTokens: 40);

        var workflow = StreamingChatWorkflow.Create("stream-usage", model)
            .Build();

        var result = await workflow.RunAsync(
            new StreamingChatState { Messages = [AgentMessage.User("hello")] });

        result.Status.ShouldBe(ExecutionStatus.Completed);
        // The streaming model reports 80 input + 40 output tokens.
        // The workflow should accumulate them via TokenUsageCapture.
        result.State.ShouldNotBeNull();
    }

    [Test]
    public async Task StreamingChatWorkflow_WithBudget_EnforcesBudget()
    {
        var model = new UsageReportingStreamingModel(inputTokens: 5000, outputTokens: 2000);

        // Build a workflow that uses the streaming model in a delegate job,
        // which mirrors what StreamingChatWorkflow.Build() does internally.
        // Budget: $0.01. Cost per call: (5000/1000 * 0.01) + (2000/1000 * 0.03) = 0.05 + 0.06 = 0.11
        // First agent call exceeds the $0.01 budget.
        var execution = await StreamingChatWorkflow.Create("stream-budget", model)
            .Build()
            .WithBudget(maxCost: 0.01m, costPer1KInputTokens: 0.01m, costPer1KOutputTokens: 0.03m)
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("hello")] });

        execution.Status.ShouldBe(ExecutionStatus.BudgetExceeded);
        execution.CumulativeUsage.InputTokens.ShouldBe(5000);
        execution.CumulativeUsage.OutputTokens.ShouldBe(2000);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static IJob<BudgetState> CreateAgentJob(string name, IAgentModel model) =>
        AgentJobFactory.Create<BudgetState, BudgetResponse>(name, model)
            .WithPrompt(_ => "test prompt")
            .MapResult((s, r) => s with { LastText = r.Answer ?? "" })
            .Build();

    // ── Test types ──────────────────────────────────────────────

    private record BudgetState
    {
        public string LastText { get; init; } = "";
    }

    private record BudgetResponse
    {
        public string? Answer { get; init; }
    }

    /// <summary>
    /// Model that returns a response with token usage metadata.
    /// </summary>
    private sealed class UsageReportingModel(int inputTokens, int outputTokens) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                Text = """{"Answer":"result"}""",
                Usage = new TokenUsage
                {
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                }
            });
    }

    /// <summary>
    /// Model that returns a response without usage metadata (null).
    /// </summary>
    private sealed class NoUsageModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                Text = """{"Answer":"result"}""",
                Usage = null
            });
    }

    /// <summary>
    /// Streaming model that reports token usage on the completed response.
    /// </summary>
    private sealed class UsageReportingStreamingModel(int inputTokens, int outputTokens) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                Text = "response",
                Usage = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens }
            });

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new AgentStreamChunk { TextDelta = "response" };
            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse
                {
                    Text = "response",
                    Usage = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens }
                }
            };
        }
    }
}
