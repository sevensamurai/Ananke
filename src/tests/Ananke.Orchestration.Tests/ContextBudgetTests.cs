using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Covers the reconciliation of the numbers that each used to claim to be "the context budget":
/// the strategy's constructor value, the caller's allocation, and the selected model's real window.
/// </summary>
[TestFixture]
public class ContextBudgetTests
{
    // ── Resolution ───────────────────────────────────────────────

    [Test]
    public void Resolve_Unspecified_UsesTheConfiguredValue()
    {
        ContextBudget.Unspecified.Resolve(4_096).ShouldBe(4_096);
    }

    [Test]
    public void Resolve_AllocationSupplied_OverridesTheConfiguredValue()
    {
        new ContextBudget { MaxTokens = 1_000 }.Resolve(4_096).ShouldBe(1_000);
    }

    [Test]
    public void Resolve_WindowSmallerThanConfigured_CapsToTheWindow()
    {
        // The defect this fixes: a strategy built for a large window, composed with a router that
        // may pick a small local model, previously overflowed that model silently.
        new ContextBudget { ModelContextTokens = 4_096 }
            .Resolve(200_000)
            .ShouldBe(4_096);
    }

    [Test]
    public void Resolve_WindowSmallerThanAllocation_CapsToTheWindow()
    {
        new ContextBudget { MaxTokens = 100_000, ModelContextTokens = 8_192 }
            .Resolve(4_096)
            .ShouldBe(8_192);
    }

    [Test]
    public void Resolve_SmallConfiguredOnLargeWindow_IsNotInflated()
    {
        // Capacity is a cap, never a target. Raising a small budget to fill a large window would be
        // an allocation decision, and nothing at this layer knows what the work is for.
        new ContextBudget { ModelContextTokens = 200_000 }
            .Resolve(4_096)
            .ShouldBe(4_096);
    }

    // ── Projection reporting ─────────────────────────────────────

    [Test]
    public async Task SlidingWindow_WhenNothingIsDropped_ReportsAnUnchangedProjection()
    {
        var strategy = new SlidingWindowContextStrategy(maxTokens: 10_000);

        var projection = await strategy.ApplyAsync(
            [AgentMessage.User("short")], null, ContextBudget.Unspecified);

        projection.Compacted.ShouldBeFalse();
        projection.ShadowedCount.ShouldBe(0);
        projection.Reason.ShouldBe(ContextShadowReason.None);
        projection.AppliedBudget.ShouldBe(10_000);
    }

    [Test]
    public async Task SlidingWindow_WhenMessagesAreDropped_ReportsWhatWasWithheld()
    {
        var messages = Enumerable.Range(0, 20)
            .Select(i => AgentMessage.User($"message {i}: " + new string('x', 200)))
            .ToList();
        var strategy = new SlidingWindowContextStrategy(maxTokens: 200);

        var projection = await strategy.ApplyAsync(messages, null, ContextBudget.Unspecified);

        projection.Compacted.ShouldBeTrue();
        projection.Reason.ShouldBe(ContextShadowReason.Dropped);
        projection.ShadowedCount.ShouldBe(messages.Count - projection.Messages.Count);
        // Compaction is lossy; a return type that cannot say how much it dropped makes it unauditable.
        projection.ShadowedTokens.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task SlidingWindow_CallerBudget_DrivesCompaction_NotTheConstructorValue()
    {
        // The structural fix: a strategy instance can now serve a router whose candidates have
        // different windows, because the budget arrives per call instead of at construction.
        var messages = Enumerable.Range(0, 20)
            .Select(i => AgentMessage.User($"message {i}: " + new string('x', 200)))
            .ToList();
        var strategy = new SlidingWindowContextStrategy(maxTokens: 100_000);

        var roomy = await strategy.ApplyAsync(messages, null, ContextBudget.Unspecified);
        var cramped = await strategy.ApplyAsync(
            messages, null, new ContextBudget { ModelContextTokens = 200 });

        roomy.Compacted.ShouldBeFalse();
        cramped.Compacted.ShouldBeTrue();
        cramped.Messages.Count.ShouldBeLessThan(roomy.Messages.Count);
    }

    [Test]
    public async Task SlidingWindow_SystemPromptAloneExceedsBudget_ReportsTheDroppedRemainder()
    {
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("first"), AgentMessage.User("second"), AgentMessage.User("third")
        };
        var strategy = new SlidingWindowContextStrategy(maxTokens: 4);

        var projection = await strategy.ApplyAsync(
            messages, new string('s', 400), ContextBudget.Unspecified);

        projection.Messages.Count.ShouldBe(1);
        projection.ShadowedCount.ShouldBe(2);
        projection.Reason.ShouldBe(ContextShadowReason.Dropped);
    }

    [Test]
    public async Task Summarizing_WhenItSummarizes_ReportsTheSummarizedReason()
    {
        var messages = Enumerable.Range(0, 12)
            .Select(i => AgentMessage.User($"message {i}: " + new string('y', 200)))
            .ToList();
        var strategy = new SummarizingContextStrategy(
            new StubSummarizer(), thresholdTokens: 100, recentMessageCount: 2);

        var projection = await strategy.ApplyAsync(messages, null, ContextBudget.Unspecified);

        projection.Reason.ShouldBe(ContextShadowReason.Summarized);
        projection.ShadowedCount.ShouldBe(10);
        projection.ShadowedTokens.ShouldBeGreaterThan(0);
    }

    // ── End to end through the engine ────────────────────────────

    [Test]
    public async Task AgentJob_CompactsAgainstTheRoutedModelsRealWindow()
    {
        // The whole point of the slice: the strategy is configured for a window far larger than the
        // model the router actually picks, and the assembly is still capped by the real one.
        var router = new CapabilityModelRouter(RoutingStrategy.BestFit)
            .AddModel(ModelProfile.ForTier("tiny", new EchoModel(), ModelTier.FullModel, 64));
        var observer = new CompactionRecorder();

        var agent = AgentJobFactory.Create<BudgetState>("budgeted", router.ToAgentModel())
            .WithPrompt(s => s.Input)
            .WithContextStrategy(new SlidingWindowContextStrategy(maxTokens: 200_000))
            .MapResult((s, text) => s with { Output = text })
            .Build();

        using (ContextObserving.BeginScope(observer))
            await agent.ExecuteAsync(new BudgetState { Input = new string('z', 4_000) });

        observer.Records.ShouldNotBeEmpty();
        var record = observer.Records[0];
        record.Budget.ModelName.ShouldBe("tiny");
        record.Budget.ModelContextTokens.ShouldBe(64);
        // 200 000 was configured; 64 is what the model will actually take.
        record.Projection.AppliedBudget.ShouldBe(64);
    }

    [Test]
    public async Task AgentJob_WithoutObserver_RecordsNothing()
    {
        var router = new CapabilityModelRouter(RoutingStrategy.BestFit)
            .AddModel(ModelProfile.ForTier("tiny", new EchoModel(), ModelTier.FullModel, 64));
        var observer = new CompactionRecorder();

        var agent = AgentJobFactory.Create<BudgetState>("unobserved", router.ToAgentModel())
            .WithPrompt(s => s.Input)
            .WithContextStrategy(new SlidingWindowContextStrategy(maxTokens: 200_000))
            .MapResult((s, text) => s with { Output = text })
            .Build();

        await agent.ExecuteAsync(new BudgetState { Input = "hello" });

        observer.Records.ShouldBeEmpty();
    }

    private sealed record BudgetState
    {
        public string Input { get; init; } = string.Empty;
        public string Output { get; init; } = string.Empty;
    }

    private sealed class CompactionRecorder : IContextObserver
    {
        private readonly List<ContextCompactionRecord> _records = [];

        public IReadOnlyList<ContextCompactionRecord> Records => _records;

        public ValueTask OnContextAssembledAsync(
            ContextObservation observation, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask OnContextCompactedAsync(
            ContextCompactionRecord record, CancellationToken ct = default)
        {
            lock (_records)
                _records.Add(record);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EchoModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "done" });
    }

    private sealed class StubSummarizer : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "summary" });
    }
}
