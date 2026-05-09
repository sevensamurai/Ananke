using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Tools.Routing;

[TestFixture]
public class LlmRouterStageTests
{
    // ── Required cases ───────────────────────────────────────────────

    [Test]
    public async Task Returns_LowConfidence_OnUnparseableResponse()
    {
        var model = FakeModel.Always("this is not json at all");
        var stage = new LlmRouterStage(model, maxRetries: 1);
        var request = MakeRequest(MakeCandidates("search", "calc"));

        var decision = await stage.RouteAsync(request);

        decision.Confidence.ShouldBe(RoutingConfidence.Low);
        // Escalation: original candidates returned unchanged
        decision.SelectedTools.Count.ShouldBe(2);
    }

    [Test]
    public async Task Drops_HallucinatedToolNames()
    {
        var json = """{"useTools":true,"selectedToolNames":["search","ghost_tool"],"confidence":"high"}""";
        var model = FakeModel.Always(json);
        var stage = new LlmRouterStage(model);
        var request = MakeRequest(MakeCandidates("search", "calc")); // no "ghost_tool"

        var decision = await stage.RouteAsync(request);

        decision.SelectedTools.ShouldHaveSingleItem();
        decision.SelectedTools[0].ToolName.ShouldBe("search");
    }

    [Test]
    public async Task Honours_UseToolsFalse_FromCheapModel()
    {
        var json = """{"useTools":false,"selectedToolNames":[],"confidence":"high"}""";
        var model = FakeModel.Always(json);
        var stage = new LlmRouterStage(model);

        var decision = await stage.RouteAsync(MakeRequest(MakeCandidates("a", "b")));

        decision.UseTools.ShouldBeFalse();
        decision.SelectedTools.ShouldBeEmpty();
    }

    [Test]
    public async Task Forwards_CancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var model = new CancellationCapturingModel();
        var stage = new LlmRouterStage(model);

        await stage.RouteAsync(MakeRequest(MakeCandidates("a")), cts.Token);

        model.ReceivedToken.ShouldBe(cts.Token);
    }

    // ── Retry behaviour ───────────────────────────────────────────────

    [Test]
    public async Task Retry_SecondAttemptSucceeds_ReturnsDecision()
    {
        var validJson = """{"useTools":true,"selectedToolNames":["calc"],"confidence":"medium"}""";
        // First call returns garbage; second returns valid JSON
        var model = FakeModel.Sequence("bad json", validJson);
        var stage = new LlmRouterStage(model, maxRetries: 1);
        var request = MakeRequest(MakeCandidates("calc", "search"));

        var decision = await stage.RouteAsync(request);

        decision.Confidence.ShouldBe(RoutingConfidence.Medium);
        decision.SelectedTools.ShouldHaveSingleItem();
        decision.SelectedTools[0].ToolName.ShouldBe("calc");
    }

    [Test]
    public async Task Retry_BothAttemptsFail_EscalatesWithOriginalCandidates()
    {
        var model = FakeModel.Always("¬valid");
        var stage = new LlmRouterStage(model, maxRetries: 1);
        var candidates = MakeCandidates("a", "b", "c");

        var decision = await stage.RouteAsync(MakeRequest(candidates));

        decision.Confidence.ShouldBe(RoutingConfidence.Low);
        decision.SelectedTools.Count.ShouldBe(3);
        decision.Rationale.ShouldNotBeNullOrEmpty();
    }

    // ── Markdown fence stripping ──────────────────────────────────────

    [Test]
    public async Task ParsesResponse_WrappedInMarkdownFences()
    {
        var json = """
            ```json
            {"useTools":true,"selectedToolNames":["search"],"confidence":"high"}
            ```
            """;
        var model = FakeModel.Always(json);
        var stage = new LlmRouterStage(model);

        var decision = await stage.RouteAsync(MakeRequest(MakeCandidates("search")));

        decision.UseTools.ShouldBeTrue();
        decision.Confidence.ShouldBe(RoutingConfidence.High);
    }

    // ── Confidence mapping ────────────────────────────────────────────

    [Test]
    public async Task UnknownConfidenceString_MapsToLow()
    {
        var json = """{"useTools":true,"selectedToolNames":["a"],"confidence":"very_sure"}""";
        var model = FakeModel.Always(json);
        var stage = new LlmRouterStage(model);

        var decision = await stage.RouteAsync(MakeRequest(MakeCandidates("a")));

        decision.Confidence.ShouldBe(RoutingConfidence.Low);
    }

    // ── Default prompt template smoke test ───────────────────────────

    [Test]
    public void DefaultRoutingPromptTemplate_SystemPrompt_ContainsCandidateNames()
    {
        var template = new DefaultRoutingPromptTemplate();
        var request = MakeRequest(MakeCandidates("web_search", "calculator"));

        var system = template.RenderSystemPrompt(request);

        system.ShouldContain("web_search");
        system.ShouldContain("calculator");
        system.ShouldContain("useTools");
    }

    [Test]
    public void DefaultRoutingPromptTemplate_UserPrompt_ContainsUserMessage()
    {
        var template = new DefaultRoutingPromptTemplate();
        var request = new ToolRoutingRequest
        {
            UserMessage = "what is the weather today?",
            Candidates = [],
            ConversationDigest = ["User: hello", "Assistant: hi"],
        };

        var user = template.RenderUserPrompt(request);

        user.ShouldContain("what is the weather today?");
        user.ShouldContain("User: hello");
    }

    [Test]
    public void DefaultRoutingPromptTemplate_RetryPrompt_ContainsPreviousResponse()
    {
        var template = new DefaultRoutingPromptTemplate();
        var request = MakeRequest([]);
        var badResponse = "oops not json";

        var retry = template.RenderRetrySystemPrompt(request, badResponse);

        retry.ShouldContain(badResponse);
        retry.ShouldContain("useTools");
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static ToolRoutingRequest MakeRequest(IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = "test query", Candidates = candidates };

    private static IReadOnlyList<ToolMemoryEntry> MakeCandidates(params string[] names) =>
        names.Select(n => new ToolMemoryEntry { ToolName = n, KitName = "kit", Description = $"Desc {n}" })
             .ToList();

    // ── Inline fakes ──────────────────────────────────────────────────

    private sealed class FakeModel(Queue<string> responses) : IAgentModel
    {
        public static FakeModel Always(string text) =>
            new(new Queue<string>(Enumerable.Repeat(text, 10)));

        public static FakeModel Sequence(params string[] texts) =>
            new(new Queue<string>(texts));

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            var text = responses.Count > 1 ? responses.Dequeue() : responses.Peek();
            return Task.FromResult(new AgentResponse { Text = text });
        }
    }

    private sealed class CancellationCapturingModel : IAgentModel
    {
        public CancellationToken ReceivedToken { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            ReceivedToken = ct;
            return Task.FromResult(new AgentResponse
            {
                Text = """{"useTools":true,"selectedToolNames":[],"confidence":"high"}""",
            });
        }
    }
}
