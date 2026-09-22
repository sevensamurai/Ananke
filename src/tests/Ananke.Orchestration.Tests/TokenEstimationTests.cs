using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Pins the behaviour that changed when the two token estimators were unified.
/// </summary>
/// <remarks>
/// <para>
/// Two defects were folded into one fix. <b>The counter double-counted multimodal text</b>:
/// <see cref="AgentMessage.Content"/> is *computed* by concatenating <see cref="TextPart"/> entries,
/// so adding both charged such messages twice and made both shipped strategies compact earlier than
/// the real prompt size warranted. <b>And the agent engine carried its own private estimator</b> with
/// different rounding, so "how big is this request" depended on who asked.
/// </para>
/// <para>
/// The fix moved a threshold, which is why these tests exist: they assert the *consequences* rather
/// than the arithmetic, so a regression shows up as compaction happening at the wrong moment rather
/// than as an unexplained number.
/// </para>
/// </remarks>
[TestFixture]
public class TokenEstimationTests
{
    // ── The same text, expressed two ways, must measure the same ──

    [Test]
    public void RequestTokenEstimator_PartsAndContent_MeasureIdentically()
    {
        var text = new string('a', 1_200);
        var viaParts = new AgentRequest
        {
            Messages = [new AgentMessage { Role = AgentRole.User, Parts = [new TextPart(text)] }]
        };
        var viaContent = new AgentRequest { Messages = [AgentMessage.User(text)] };

        RequestTokenEstimator.Estimate(viaParts)
            .ShouldBe(RequestTokenEstimator.Estimate(viaContent));
    }

    [Test]
    public async Task SlidingWindow_MultimodalAndPlainMessages_CompactIdentically()
    {
        var text = new string('a', 800);
        var asParts = Enumerable.Range(0, 6)
            .Select(_ => new AgentMessage { Role = AgentRole.User, Parts = [new TextPart(text)] })
            .ToList();
        var asContent = Enumerable.Range(0, 6)
            .Select(_ => AgentMessage.User(text))
            .ToList();
        var strategy = new SlidingWindowContextStrategy(maxTokens: 500);

        var fromParts = await strategy.ApplyAsync(asParts, null, ContextBudget.Unspecified);
        var fromContent = await strategy.ApplyAsync(asContent, null, ContextBudget.Unspecified);

        fromParts.Messages.Count.ShouldBe(fromContent.Messages.Count);
        fromParts.ShadowedCount.ShouldBe(fromContent.ShadowedCount);
        fromParts.ShadowedTokens.ShouldBe(fromContent.ShadowedTokens);
    }

    // ── The moved threshold, pinned from both sides ───────────────

    [Test]
    public async Task SlidingWindow_MultimodalMessagesThatFit_AreNoLongerCompacted()
    {
        // 400 chars ≈ 100 tokens each, so 200 for the pair. Under the double-count they measured
        // ~400 and were compacted against a 250 budget; measured once, they fit.
        var messages = Enumerable.Range(0, 2)
            .Select(_ => new AgentMessage
            {
                Role = AgentRole.User,
                Parts = [new TextPart(new string('a', 400))]
            })
            .ToList();
        var strategy = new SlidingWindowContextStrategy(maxTokens: 250);

        var projection = await strategy.ApplyAsync(messages, null, ContextBudget.Unspecified);

        projection.Compacted.ShouldBeFalse();
        projection.Messages.Count.ShouldBe(2);
    }

    [Test]
    public async Task SlidingWindow_MultimodalMessagesThatDoNotFit_AreStillCompacted()
    {
        // The counterpart: the fix must not make the strategy permissive, only accurate.
        var messages = Enumerable.Range(0, 8)
            .Select(_ => new AgentMessage
            {
                Role = AgentRole.User,
                Parts = [new TextPart(new string('a', 400))]
            })
            .ToList();
        var strategy = new SlidingWindowContextStrategy(maxTokens: 250);

        var projection = await strategy.ApplyAsync(messages, null, ContextBudget.Unspecified);

        projection.Compacted.ShouldBeTrue();
        projection.Reason.ShouldBe(ContextShadowReason.Dropped);
    }

    [Test]
    public async Task Summarizing_MultimodalMessagesThatFit_AreNoLongerSummarized()
    {
        var messages = Enumerable.Range(0, 6)
            .Select(_ => new AgentMessage
            {
                Role = AgentRole.User,
                Parts = [new TextPart(new string('a', 400))]
            })
            .ToList();
        // 6 × ~100 = ~600 measured once; ~1 200 under the double-count.
        var strategy = new SummarizingContextStrategy(
            new ThrowingSummarizer(), thresholdTokens: 800, recentMessageCount: 2);

        var projection = await strategy.ApplyAsync(messages, null, ContextBudget.Unspecified);

        // ThrowingSummarizer proves no summarisation call was made at all.
        projection.Reason.ShouldBe(ContextShadowReason.None);
        projection.Messages.Count.ShouldBe(6);
    }

    // ── One estimator, including inside the engine's gate ─────────

    [Test]
    public async Task ContextGate_MeasuresWithTheSharedEstimator()
    {
        // The engine's gate must agree with RequestTokenEstimator to the token. Rather than guess
        // what history the engine accumulates, capture it: the strategy sees exactly the list the
        // gate then measures, and the model sees the tools and system prompt that go with it.
        var prompt = new string('q', 4_000);
        var spy = new RecordingStrategy();
        var model = ToolThenAnswerModel();

        var probe = AgentJobFactory.Create<GateState>("probe", model)
            .WithPrompt(s => s.Input)
            .WithTools(EchoKit())
            .WithContextLimit(int.MaxValue)
            .WithContextStrategy(spy)
            .MapResult((s, text) => s with { Output = text })
            .Build();

        await probe.ExecuteAsync(new GateState { Input = prompt });

        // The gate fires on the first round whose estimate exceeds the limit, so the boundary is
        // the largest round.
        var boundary = spy.Rounds
            .Select(msgs => RequestTokenEstimator.Estimate(new AgentRequest
            {
                SystemPrompt = model.LastSystemPrompt,
                Messages = msgs,
                Tools = model.LastTools
            }))
            .Max();

        boundary.ShouldBeGreaterThan(0);

        await Should.NotThrowAsync(
            () => ToolJob("gate-fits", boundary).ExecuteAsync(new GateState { Input = prompt }));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => ToolJob("gate-tight", boundary - 1).ExecuteAsync(new GateState { Input = prompt }));
        ex.Message.ShouldContain("exceeds the configured limit");
    }

    /// <summary>Passes messages through untouched, recording each list the engine offers it.</summary>
    private sealed class RecordingStrategy : IContextStrategy
    {
        private readonly List<IReadOnlyList<AgentMessage>> _rounds = [];

        public IReadOnlyList<IReadOnlyList<AgentMessage>> Rounds => _rounds;

        public Task<ContextProjection> ApplyAsync(
            IReadOnlyList<AgentMessage> messages,
            string? systemPrompt,
            ContextBudget budget,
            CancellationToken ct = default)
        {
            _rounds.Add([.. messages]);
            return Task.FromResult(ContextProjection.Unchanged(messages, budget.Resolve(int.MaxValue)));
        }
    }

    /// <summary>
    /// Regression test for a defect this slice found: <c>WithContextLimit</c> used to be
    /// <b>inert on an agent with no tools</b>. Both tool-loop enforcement points sit inside the
    /// loop, and a toolless job runs a different path that never reached them — so a configured
    /// safety limit silently did nothing at all.
    /// </summary>
    [Test]
    public async Task ContextLimit_IsEnforcedOnAJobWithNoTools()
    {
        var job = AgentJobFactory.Create<GateState>("toolless-tight", new EchoModel())
            .WithPrompt(s => s.Input)
            .WithContextLimit(1)
            .MapResult((s, text) => s with { Output = text })
            .Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => job.ExecuteAsync(new GateState { Input = new string('q', 40_000) }));

        ex.Message.ShouldContain("exceeds the configured limit");
        // The message must not claim a tool round that never happened.
        ex.Message.ShouldContain("before the model call");
    }

    [Test]
    public async Task ContextLimit_OnAJobWithNoTools_AllowsWhatFits()
    {
        var prompt = "a short prompt";
        var estimate = RequestTokenEstimator.Estimate(
            new AgentRequest { Messages = [AgentMessage.User(prompt)] });

        var job = AgentJobFactory.Create<GateState>("toolless-fits", new EchoModel())
            .WithPrompt(s => s.Input)
            .WithContextLimit(estimate)
            .MapResult((s, text) => s with { Output = text })
            .Build();

        var result = await job.ExecuteAsync(new GateState { Input = prompt });

        result.Output.ShouldBe("done");
    }

    private const string ToolPayload = "tool output";

    private static TextAgentJob<GateState> ToolJob(string name, int contextLimit) =>
        AgentJobFactory.Create<GateState>(name, ToolThenAnswerModel())
            .WithPrompt(s => s.Input)
            .WithTools(EchoKit())
            .WithContextLimit(contextLimit)
            .MapResult((s, text) => s with { Output = text })
            .Build();

    private static ToolKit EchoKit() =>
        new ToolKit("k").AddTool("echo", "Returns a fixed payload", () => ToolResult.Ok(ToolPayload));

    private static SequencedModel ToolThenAnswerModel() => SequencedModel.Of(
        new AgentResponse { Text = "r1", ToolCalls = [new AgentToolCall("c1", "echo", "{}")] },
        new AgentResponse { Text = "done" });

    private sealed class SequencedModel : IAgentModel
    {
        private readonly AgentResponse[] _responses;
        private int _index;

        private SequencedModel(AgentResponse[] responses) => _responses = responses;

        public static SequencedModel Of(params AgentResponse[] responses) => new(responses);

        /// <summary>The tool definitions the engine last sent, so a test can rebuild its request.</summary>
        public IReadOnlyList<AgentTool>? LastTools { get; private set; }

        /// <summary>The system prompt the engine last sent.</summary>
        public string? LastSystemPrompt { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            LastTools = request.Tools;
            LastSystemPrompt = request.SystemPrompt;
            return Task.FromResult(_responses[Math.Min(_index++, _responses.Length - 1)]);
        }
    }

    private sealed record GateState
    {
        public string Input { get; init; } = string.Empty;
        public string Output { get; init; } = string.Empty;
    }

    private sealed class EchoModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "done" });
    }

    /// <summary>Fails loudly if summarisation is attempted when it should not be.</summary>
    private sealed class ThrowingSummarizer : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("Summarisation should not have been triggered.");
    }
}
