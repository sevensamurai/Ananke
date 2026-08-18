using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Pins the three behaviours where <see cref="AgentJob{TState, TResponse}"/> and
/// <see cref="TextAgentJob{TState}"/> had drifted apart. The two types share ~85% of their
/// implementation by copy-paste with nothing keeping them in sync, so each divergence below
/// was a real defect in one of the pair while the other was correct.
/// </summary>
[TestFixture]
public class AgentJobDivergenceTests
{
    public record DivergenceState
    {
        public string Input { get; init; } = "";
        public string Output { get; init; } = "";
    }

    public sealed record Answer
    {
        public string Output { get; init; } = "";
    }

    // ── Context limit measures live messages, not the previous round's request ──

    /// <summary>
    /// <c>TextAgentJob</c> used to call <c>EstimateTokens(request)</c>, reusing the
    /// <see cref="AgentRequest"/> built for the *previous* round. That happened to be correct
    /// while <c>request.Messages</c> still aliased the live list — but once an
    /// <see cref="IContextStrategy"/> is configured the loop reassigns
    /// <c>request with { Messages = compacted }</c>, breaking the alias, and every later check
    /// measured a stale snapshot. <c>AgentJob</c> always built a fresh pre-flight request.
    /// </summary>
    [Test]
    public async Task TextAgentJob_ContextLimitWithStrategy_MeasuresMessagesAddedSinceLastRound()
    {
        var hugeToolOutput = new string('x', 20_000);

        var model = SimulatedModel.Sequence(
            new AgentResponse { Text = "r1", ToolCalls = [new AgentToolCall("c1", "big", "{}")] },
            new AgentResponse { Text = "r2", ToolCalls = [new AgentToolCall("c2", "big", "{}")] },
            new AgentResponse { Text = "done" });

        var tools = new ToolKit("bulk")
            .AddTool("big", "Returns a lot of text", () => ToolResult.Ok(hugeToolOutput));

        // Two tool rounds at ~5,000 estimated tokens each: the first round is under the limit,
        // the running total after the second is not. Reading only the first round's snapshot
        // (the old behaviour) stays under 7,000 and never trips.
        var agent = AgentJobFactory.Create<DivergenceState>("bulky", model)
            .WithPrompt(s => s.Input)
            .WithTools(tools)
            .WithContextLimit(7_000)
            .WithContextStrategy(new CopyingContextStrategy())
            .MapResult((s, text) => s with { Output = text })
            .Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => agent.ExecuteAsync(new DivergenceState { Input = "go" }));

        ex.Message.ShouldContain("exceeds the configured limit");
    }

    // ── Budget build-time validation sees text jobs too ──────────────────────────

    /// <summary>
    /// <c>Workflow.Build()</c> rejects a budget with no cost-rate source unless some job reports
    /// <c>IProfileAwareJob.HasProfileAwareModel</c>. <c>TextAgentJob</c> did not implement the
    /// interface at all, so a text job on a cost-resolving router was invisible to that check and
    /// the workflow failed to build even though rates were available.
    /// </summary>
    [Test]
    public void TextAgentJob_OnCostResolvingRouter_SatisfiesBudgetBuildCheck()
    {
        var router = new CostResolvingRouter(SimulatedModel.Fixed("ok"));

        var agent = AgentJobFactory.Create<DivergenceState>("routed", router)
            .WithPrompt(s => s.Input)
            .MapResult((s, text) => s with { Output = text })
            .Build();

        Should.NotThrow(() => new Workflow<DivergenceState>("budgeted")
            .Job("routed", agent)
            .Then("routed", Workflow.End)
            .WithBudget(maxCost: 1.00m)
            .Build());
    }

    [Test]
    public void AgentJob_WithoutCostResolvingRouter_StillFailsBudgetBuildCheck()
    {
        // Guards the fix above from becoming a blanket "always profile-aware": a plain model
        // has no rates to offer, so the build check must still fire.
        var agent = AgentJobFactory.Create<DivergenceState>("plain", SimulatedModel.Fixed("ok"))
            .WithPrompt(s => s.Input)
            .MapResult((s, text) => s with { Output = text })
            .Build();

        Should.Throw<InvalidOperationException>(() => new Workflow<DivergenceState>("budgeted")
            .Job("plain", agent)
            .Then("plain", Workflow.End)
            .WithBudget(maxCost: 1.00m)
            .Build());
    }

    // ── Conversation memory records both sides of the exchange ───────────────────

    /// <summary>
    /// <c>AgentJob.ExecuteStructuredAsync</c> never appended the model's reply to the message
    /// list, so a structured agent using <c>WithMemory()</c> persisted the user prompt but not
    /// its own answer — the next turn reloaded a one-sided history.
    /// <c>TextAgentJob.ExecutePlainAsync</c> already did this correctly.
    /// </summary>
    [Test]
    public async Task AgentJob_WithMemory_PersistsAssistantReplyNotJustUserPrompt()
    {
        var memory = new InMemoryConversationMemory();
        var model = SimulatedModel.Fixed("""{"Output":"blue"}""");

        var agent = AgentJobFactory.Create<DivergenceState, Answer>("structured", model)
            .WithPrompt(s => s.Input)
            .WithMemory(memory, _ => "session-1")
            .MapResult((s, r) => s with { Output = r.Output })
            .Build();

        var result = await agent.ExecuteAsync(new DivergenceState { Input = "fav colour?" });
        result.Output.ShouldBe("blue");

        var history = await memory.GetHistoryAsync("session-1");
        history.Count.ShouldBe(2);
        history[0].Role.ShouldBe(AgentRole.User);
        history[0].Content.ShouldBe("fav colour?");
        history[1].Role.ShouldBe(AgentRole.Assistant);
        history[1].Content.ShouldBe("""{"Output":"blue"}""");
    }

    [Test]
    public async Task TextAgentJob_WithMemory_PersistsAssistantReply()
    {
        // The counterpart that was already correct — pinned so the eventual dedup of the two
        // types cannot silently regress it.
        var memory = new InMemoryConversationMemory();
        var model = SimulatedModel.Fixed("blue");

        var agent = AgentJobFactory.Create<DivergenceState>("text", model)
            .WithPrompt(s => s.Input)
            .WithMemory(memory, _ => "session-1")
            .MapResult((s, text) => s with { Output = text })
            .Build();

        await agent.ExecuteAsync(new DivergenceState { Input = "fav colour?" });

        var history = await memory.GetHistoryAsync("session-1");
        history.Count.ShouldBe(2);
        history[0].Role.ShouldBe(AgentRole.User);
        history[1].Role.ShouldBe(AgentRole.Assistant);
        history[1].Content.ShouldBe("blue");
    }

    // ── ContextLimitMode: pre- vs post-compaction, both job types ────────────────

    // Each pair below uses a strategy that compacts far below the limit while the raw history
    // sits well above it, so the two modes give opposite outcomes on identical input.

    [Test]
    public async Task TextAgentJob_ContextLimitDefaultsToPostCompaction_StrategyPreventsTheThrow()
    {
        var agent = AgentJobFactory.Create<DivergenceState>("text-post", TwoRoundTextModel())
            .WithPrompt(s => s.Input)
            .WithTools(BulkToolKit())
            .WithContextLimit(7_000) // no mode argument — must default to PostCompaction
            .WithContextStrategy(new TruncatingContextStrategy())
            .MapResult((s, text) => s with { Output = text })
            .Build();

        var result = await agent.ExecuteAsync(new DivergenceState { Input = "go" });

        result.Output.ShouldBe("done");
    }

    [Test]
    public async Task TextAgentJob_ContextLimitPreCompaction_ThrowsOnRawHistory()
    {
        var agent = AgentJobFactory.Create<DivergenceState>("text-pre", TwoRoundTextModel())
            .WithPrompt(s => s.Input)
            .WithTools(BulkToolKit())
            .WithContextLimit(7_000, ContextLimitMode.PreCompaction)
            .WithContextStrategy(new TruncatingContextStrategy())
            .MapResult((s, text) => s with { Output = text })
            .Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => agent.ExecuteAsync(new DivergenceState { Input = "go" }));

        ex.Message.ShouldContain("exceeds the configured limit");
    }

    [Test]
    public async Task AgentJob_ContextLimitDefaultsToPostCompaction_StrategyPreventsTheThrow()
    {
        var agent = AgentJobFactory.Create<DivergenceState, Answer>("structured-post", TwoRoundStructuredModel())
            .WithPrompt(s => s.Input)
            .WithTools(BulkToolKit())
            .WithContextLimit(7_000)
            .WithContextStrategy(new TruncatingContextStrategy())
            .MapResult((s, r) => s with { Output = r.Output })
            .Build();

        var result = await agent.ExecuteAsync(new DivergenceState { Input = "go" });

        result.Output.ShouldBe("done");
    }

    [Test]
    public async Task AgentJob_ContextLimitPreCompaction_ThrowsOnRawHistory()
    {
        var agent = AgentJobFactory.Create<DivergenceState, Answer>("structured-pre", TwoRoundStructuredModel())
            .WithPrompt(s => s.Input)
            .WithTools(BulkToolKit())
            .WithContextLimit(7_000, ContextLimitMode.PreCompaction)
            .WithContextStrategy(new TruncatingContextStrategy())
            .MapResult((s, r) => s with { Output = r.Output })
            .Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => agent.ExecuteAsync(new DivergenceState { Input = "go" }));

        ex.Message.ShouldContain("exceeds the configured limit");
    }

    /// <summary>~40,000 characters of tool output — roughly 10,000 estimated tokens, well over
    /// the 7,000 limit the mode tests configure.</summary>
    private static ToolKit BulkToolKit() =>
        new ToolKit("bulk").AddTool("big", "Returns a lot of text",
            () => ToolResult.Ok(new string('x', 40_000)));

    private static SimulatedModel TwoRoundTextModel() => SimulatedModel.Sequence(
        new AgentResponse { Text = "r1", ToolCalls = [new AgentToolCall("c1", "big", "{}")] },
        new AgentResponse { Text = "done" });

    // The structured tool loop spends one extra call coercing its final answer to JSON.
    private static SimulatedModel TwoRoundStructuredModel() => SimulatedModel.Sequence(
        new AgentResponse { Text = "r1", ToolCalls = [new AgentToolCall("c1", "big", "{}")] },
        new AgentResponse { Text = "wrapping up" },
        new AgentResponse { Text = """{"Output":"done"}""" });

    // ── Fakes ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a fresh list every call without compacting anything. That is enough to break the
    /// reference aliasing between <c>AgentRequest.Messages</c> and the live message list, which
    /// is the condition under which the stale-snapshot bug appeared.
    /// </summary>
    private sealed class CopyingContextStrategy : IContextStrategy
    {
        public Task<IReadOnlyList<AgentMessage>> ApplyAsync(
            IReadOnlyList<AgentMessage> messages,
            string? systemPrompt,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentMessage>>([.. messages]);
    }

    /// <summary>
    /// Deliberately extreme compaction — keeps only the first message — so the gap between the
    /// raw history and the post-compaction payload is unambiguous in the mode tests.
    /// </summary>
    private sealed class TruncatingContextStrategy : IContextStrategy
    {
        public Task<IReadOnlyList<AgentMessage>> ApplyAsync(
            IReadOnlyList<AgentMessage> messages,
            string? systemPrompt,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentMessage>>([messages[0]]);
    }

    private sealed class CostResolvingRouter(IAgentModel model) : IModelRouter, IModelCostResolver
    {
        public IAgentModel Select(AgentRequest request) => model;

        public ModelCostRates ResolveCostRates(AgentRequest request) => ModelCostRates.Uniform(0.001m);
    }

    private sealed class SimulatedModel : IAgentModel
    {
        private readonly Queue<AgentResponse> _responses;

        private SimulatedModel(IEnumerable<AgentResponse> responses) =>
            _responses = new Queue<AgentResponse>(responses);

        public static SimulatedModel Fixed(string text) =>
            new([new AgentResponse { Text = text }]);

        public static SimulatedModel Sequence(params AgentResponse[] responses) =>
            new(responses);

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(_responses.Count > 1 ? _responses.Dequeue() : _responses.Peek());
    }
}
