using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Jobs;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class MultiModelBudgetTests
{
    // ── ModelCostRates ──────────────────────────────────────────

    [Test]
    public void ModelCostRates_Zero_HasZeroCost()
    {
        var rates = ModelCostRates.Zero;
        var usage = new TokenUsage { InputTokens = 1000, OutputTokens = 500 };
        rates.EstimateCost(usage).ShouldBe(0m);
    }

    [Test]
    public void ModelCostRates_Uniform_SetsEqualRates()
    {
        var rates = ModelCostRates.Uniform(0.10m);
        rates.CostPer1KInputTokens.ShouldBe(0.10m);
        rates.CostPer1KOutputTokens.ShouldBe(0.10m);
    }

    [Test]
    public void ModelCostRates_EstimateCost_SplitRates()
    {
        var rates = new ModelCostRates(0.01m, 0.03m);
        var usage = new TokenUsage { InputTokens = 1000, OutputTokens = 500 };
        // (1000/1000 * 0.01) + (500/1000 * 0.03) = 0.01 + 0.015 = 0.025
        rates.EstimateCost(usage).ShouldBe(0.025m);
    }

    // ── ModelProfile.GetCostRates() ─────────────────────────────

    [Test]
    public void ModelProfile_GetCostRates_SplitRates()
    {
        var profile = new ModelProfile
        {
            Name = "gpt-4.1",
            Model = new FakeModel(),
            CostPer1KInputTokens = 0.002m,
            CostPer1KOutputTokens = 0.008m
        };

        var rates = profile.GetCostRates();
        rates.CostPer1KInputTokens.ShouldBe(0.002m);
        rates.CostPer1KOutputTokens.ShouldBe(0.008m);
    }

    [Test]
    public void ModelProfile_GetCostRates_FallsBackToBlended()
    {
        var profile = new ModelProfile
        {
            Name = "gpt-4.1-mini",
            Model = new FakeModel(),
            CostPer1KTokens = 0.15m
        };

        var rates = profile.GetCostRates();
        rates.CostPer1KInputTokens.ShouldBe(0.15m);
        rates.CostPer1KOutputTokens.ShouldBe(0.15m);
    }

    [Test]
    public void ModelProfile_GetCostRates_LocalModel_ReturnsZero()
    {
        var profile = new ModelProfile
        {
            Name = "llama3.2:3b",
            Model = new FakeModel()
            // All costs default to 0
        };

        var rates = profile.GetCostRates();
        rates.ShouldBe(ModelCostRates.Zero);
    }

    // ── Multi-model budget enforcement ──────────────────────────

    [Test]
    public async Task WithBudget_MultiModel_UsesPerModelCostRates()
    {
        // Expensive model: $0.01/1K input, $0.03/1K output
        var expensiveModel = new CostAwareModel(inputTokens: 1000, outputTokens: 500);
        // Cheap model: $0.001/1K input, $0.003/1K output
        var cheapModel = new CostAwareModel(inputTokens: 1000, outputTokens: 500);

        var router = new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "expensive",
                Model = expensiveModel,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.Reasoning | ModelCapability.StructuredOutput,
                IntelligenceTier = 4,
                CostPer1KTokens = 0.03m,
                CostPer1KInputTokens = 0.01m,
                CostPer1KOutputTokens = 0.03m
            })
            .AddModel(new ModelProfile
            {
                Name = "cheap",
                Model = cheapModel,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput,
                IntelligenceTier = 1,
                CostPer1KTokens = 0.003m,
                CostPer1KInputTokens = 0.001m,
                CostPer1KOutputTokens = 0.003m
            });

        // Both jobs use the same router — routing decides which model to use
        var job1 = AgentJobFactory.Create<MultiState, MultiResponse>("job1", router)
            .WithPrompt(_ => "simple task") // routes to cheap model
            .MapResult((s, r) => s with { Text = r.Answer ?? "" })
            .Build();

        var job2 = AgentJobFactory.Create<MultiState, MultiResponse>("job2", router)
            .WithPrompt(_ => "simple task") // routes to cheap model
            .MapResult((s, r) => s with { Text = r.Answer ?? "" })
            .Build();

        // Budget that cheap model stays within but expensive would exceed
        // Cheap per-call: (1000/1000 * 0.001) + (500/1000 * 0.003) = 0.001 + 0.0015 = 0.0025
        // Two cheap calls: 0.005
        var exec = await new Workflow<MultiState>("multi-model")
            .Job("job1", job1)
            .Job("job2", job2)
            .Chain("job1", "job2", Workflow.End)
            .WithBudget(maxCost: 0.01m)
            .RunAsync(new MultiState());

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        exec.EstimatedCost.ShouldBe(0.005m);
        exec.CumulativeUsage.InputTokens.ShouldBe(2000);
    }

    [Test]
    public async Task WithBudget_MultiModel_ExceedsBudget_WithExpensiveModel()
    {
        var expensiveModel = new CostAwareModel(inputTokens: 1000, outputTokens: 500);
        var cheapModel = new CostAwareModel(inputTokens: 1000, outputTokens: 500);

        // Each model gets its own router with its profile
        var cheapRouter = new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "cheap",
                Model = cheapModel,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput,
                IntelligenceTier = 1,
                CostPer1KTokens = 0.003m,
                CostPer1KInputTokens = 0.001m,
                CostPer1KOutputTokens = 0.003m
            });

        var expensiveRouter = new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "expensive",
                Model = expensiveModel,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput,
                IntelligenceTier = 4,
                CostPer1KTokens = 2.0m,
                CostPer1KInputTokens = 1.0m,
                CostPer1KOutputTokens = 3.0m
            });

        var job1 = AgentJobFactory.Create<MultiState, MultiResponse>("job1", cheapRouter)
            .WithPrompt(_ => "simple")
            .MapResult((s, r) => s with { Text = r.Answer ?? "" })
            .Build();

        var job2 = AgentJobFactory.Create<MultiState, MultiResponse>("job2", expensiveRouter)
            .WithPrompt(_ => "complex")
            .MapResult((s, r) => s with { Text = r.Answer ?? "" })
            .Build();

        // Cheap call: (1000/1000 * 0.001) + (500/1000 * 0.003) = 0.0025
        // Expensive call: (1000/1000 * 1.0) + (500/1000 * 3.0) = 2.5
        // Budget: 1.0 → exceeds after expensive job
        var exec = await new Workflow<MultiState>("multi-model-exceed")
            .Job("job1", job1)
            .Job("job2", job2)
            .Chain("job1", "job2", Workflow.End)
            .WithBudget(maxCost: 1.0m)
            .RunAsync(new MultiState());

        exec.Status.ShouldBe(ExecutionStatus.BudgetExceeded);
        exec.EstimatedCost.ShouldBeGreaterThan(1.0m);
    }

    // ── Local (zero-cost) model budget ──────────────────────────

    [Test]
    public async Task WithBudget_LocalModel_ZeroCost_NeverExceedsBudget()
    {
        var localModel = new CostAwareModel(inputTokens: 10000, outputTokens: 5000);

        var router = new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "llama3.2:3b",
                Model = localModel,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput,
                IntelligenceTier = 1,
                // All costs default to 0 — local Ollama model
                MaxContextTokens = 128_000,
                SpeedTier = 5
            });

        var job = AgentJobFactory.Create<MultiState, MultiResponse>("local", router)
            .WithPrompt(_ => "test")
            .MapResult((s, r) => s with { Text = r.Answer ?? "" })
            .Build();

        var exec = await new Workflow<MultiState>("local-budget")
            .Job("local", job)
            .Then("local", Workflow.End)
            .WithBudget(maxCost: 0.001m) // tiny budget
            .RunAsync(new MultiState());

        // Local model is free — should complete despite tiny budget
        exec.Status.ShouldBe(ExecutionStatus.Completed);
        exec.EstimatedCost.ShouldBe(0m);
        exec.CumulativeUsage.InputTokens.ShouldBe(10000);
    }

    // ── Mixed cloud + local model ───────────────────────────────

    [Test]
    public async Task WithBudget_MixedCloudAndLocal_OnlyCloudCountsTowardBudget()
    {
        var cloudModel = new CostAwareModel(inputTokens: 1000, outputTokens: 500);
        var localModel = new CostAwareModel(inputTokens: 5000, outputTokens: 3000);

        var localRouter = new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "llama3.2:3b",
                Model = localModel,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput,
                IntelligenceTier = 1
                // zero cost
            });

        var cloudRouter = new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "gpt-4.1-mini",
                Model = cloudModel,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput,
                IntelligenceTier = 3,
                CostPer1KTokens = 0.40m,
                CostPer1KInputTokens = 0.0004m,
                CostPer1KOutputTokens = 0.0016m
            });

        var localJob = AgentJobFactory.Create<MultiState, MultiResponse>("local-task", localRouter)
            .WithPrompt(_ => "simple")
            .MapResult((s, r) => s with { Text = r.Answer ?? "" })
            .Build();

        var cloudJob = AgentJobFactory.Create<MultiState, MultiResponse>("cloud-task", cloudRouter)
            .WithPrompt(_ => "complex reasoning task")
            .MapResult((s, r) => s with { Text = r.Answer ?? "" })
            .Build();

        var exec = await new Workflow<MultiState>("mixed-models")
            .Job("local-task", localJob)
            .Job("cloud-task", cloudJob)
            .Chain("local-task", "cloud-task", Workflow.End)
            .WithBudget(maxCost: 1.0m)
            .RunAsync(new MultiState());

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        // Only cloud call contributes to cost
        // Cloud: (1000/1000 * 0.0004) + (500/1000 * 0.0016) = 0.0004 + 0.0008 = 0.0012
        exec.EstimatedCost.ShouldBeGreaterThan(0m);
        // All tokens are tracked regardless of cost
        exec.CumulativeUsage.InputTokens.ShouldBe(6000); // 5000 + 1000
    }

    // ── WithBudget(maxCost) overload validation ─────────────────

    [Test]
    public void WithBudget_MaxCostOnly_ZeroThrows()
    {
        var workflow = new Workflow<MultiState>("test");
        Should.Throw<ArgumentOutOfRangeException>(() => workflow.WithBudget(maxCost: 0));
    }

    // ── Backward compat: flat-rate WithBudget still works ───────

    [Test]
    public async Task WithBudget_FlatRate_StillWorks()
    {
        var model = new CostAwareModel(inputTokens: 1000, outputTokens: 500);

        var job = AgentJobFactory.Create<MultiState, MultiResponse>("job", model)
            .WithPrompt(_ => "test")
            .MapResult((s, r) => s with { Text = r.Answer ?? "" })
            .Build();

        // Uses flat-rate BudgetConfig (no router, no model profiles)
        var exec = await new Workflow<MultiState>("flat-rate")
            .Job("job", job)
            .Then("job", Workflow.End)
            .WithBudget(maxCost: 0.02m, costPer1KInputTokens: 0.01m, costPer1KOutputTokens: 0.03m)
            .RunAsync(new MultiState());

        // Cost: (1000/1000 * 0.01) + (500/1000 * 0.03) = 0.01 + 0.015 = 0.025 > 0.02
        exec.Status.ShouldBe(ExecutionStatus.BudgetExceeded);
        exec.EstimatedCost.ShouldBe(0.025m);
    }

    // ── CapabilityModelRouter implements IModelCostResolver ─────

    [Test]
    public void CapabilityModelRouter_ImplementsIModelCostResolver()
    {
        var router = new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "test",
                Model = new FakeModel(),
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KInputTokens = 0.01m,
                CostPer1KOutputTokens = 0.03m
            });

        var costResolver = router as IModelCostResolver;
        costResolver.ShouldNotBeNull();

        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("test")]
        };

        var rates = costResolver!.ResolveCostRates(request);
        rates.CostPer1KInputTokens.ShouldBe(0.01m);
        rates.CostPer1KOutputTokens.ShouldBe(0.03m);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private record MultiState
    {
        public string Text { get; init; } = "";
    }

    private record MultiResponse
    {
        public string? Answer { get; init; }
    }

    private sealed class FakeModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = """{"Answer":"ok"}""" });
    }

    private sealed class CostAwareModel(int inputTokens, int outputTokens) : IAgentModel
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
}
