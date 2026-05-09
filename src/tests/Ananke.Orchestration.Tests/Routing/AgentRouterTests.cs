using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Routing;

[TestFixture]
public class AgentRouterTests
{
    // ── Whitespace normalisation ──────────────────────────────────────────────

    [Test]
    public async Task RouteAsync_WhitespacePaddedResponse_NormalisedAndMatched()
    {
        // Model returns "  JobA\n" — should be trimmed and matched case-insensitively.
        var model = SimulatedModel.Fixed("  JobA\n");
        var router = BuildRouter(model, "JobA", "JobB");

        var result = await router.RouteAsync("state", CancellationToken.None);

        result.ShouldBe("JobA");
    }

    [Test]
    public async Task RouteAsync_CaseInsensitiveResponse_Matched()
    {
        var model = SimulatedModel.Fixed("joba");
        var router = BuildRouter(model, "JobA", "JobB");

        var result = await router.RouteAsync("state", CancellationToken.None);

        result.ShouldBe("JobA");
    }

    // ── Retry on hallucination ────────────────────────────────────────────────

    [Test]
    public async Task RouteAsync_HallucinatedFirstThenValid_RetriesAndSucceeds()
    {
        // First call returns a hallucination; second call returns a valid option.
        var model = SimulatedModel.Sequence(
            new AgentResponse { Text = "Summarize" },   // hallucination
            new AgentResponse { Text = "JobB" });        // valid on retry

        var router = BuildRouter(model, maxRoutingRetries: 2, "JobA", "JobB");

        var result = await router.RouteAsync("state", CancellationToken.None);

        result.ShouldBe("JobB");
    }

    [Test]
    public async Task RouteAsync_AlwaysHallucinated_ThrowsAgentRoutingException()
    {
        // All calls return a hallucinated value — exhaust MaxRoutingRetries + 1 attempts.
        var model = SimulatedModel.Fixed("DoSomethingElse");
        var router = BuildRouter(model, maxRoutingRetries: 2, "JobA", "JobB");

        var ex = await Should.ThrowAsync<AgentRoutingException>(
            () => router.RouteAsync("state", CancellationToken.None));

        ex.UnexpectedValue.ShouldBe("DoSomethingElse");
        ex.AvailableOptions.ShouldContain("JobA");
        ex.AvailableOptions.ShouldContain("JobB");
    }

    // ── AgentRoutingException message ─────────────────────────────────────────

    [Test]
    public async Task AgentRoutingException_Message_IncludesValueAndOptions()
    {
        var model = SimulatedModel.Fixed("Hallucination");
        var router = BuildRouter(model, maxRoutingRetries: 0, "Alpha", "Beta");

        var ex = await Should.ThrowAsync<AgentRoutingException>(
            () => router.RouteAsync("state", CancellationToken.None));

        ex.Message.ShouldContain("Hallucination");
        ex.Message.ShouldContain("Alpha");
        ex.Message.ShouldContain("Beta");
    }

    // ── MaxRoutingRetries = 0: no retries ─────────────────────────────────────

    [Test]
    public async Task RouteAsync_MaxRetriesZero_ThrowsImmediately()
    {
        var model = SimulatedModel.Fixed("invalid");
        var router = BuildRouter(model, maxRoutingRetries: 0, "JobA", "JobB");

        await Should.ThrowAsync<AgentRoutingException>(
            () => router.RouteAsync("state", CancellationToken.None));

        // Only 1 total call (no retries)
        model.CallCount.ShouldBe(1);
    }

    [Test]
    public async Task RouteAsync_MaxRetriesTwo_MakesUpToThreeCalls()
    {
        var model = SimulatedModel.Fixed("invalid");
        var router = BuildRouter(model, maxRoutingRetries: 2, "JobA", "JobB");

        await Should.ThrowAsync<AgentRoutingException>(
            () => router.RouteAsync("state", CancellationToken.None));

        // 1 initial + 2 retries = 3 total calls
        model.CallCount.ShouldBe(3);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AgentRouter<string> BuildRouter(
        SimulatedModel model,
        params string[] options) =>
        BuildRouter(model, maxRoutingRetries: 2, options);

    private static AgentRouter<string> BuildRouter(
        SimulatedModel model,
        int maxRoutingRetries,
        params string[] options) =>
        new AgentRouter<string>.Builder(model)
            .WithPrompt(s => s)
            .WithOptions(options)
            .WithMaxRoutingRetries(maxRoutingRetries)
            .Build();

    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class SimulatedModel : IAgentModel
    {
        private readonly Queue<AgentResponse> _responses;
        private int _callCount;

        private SimulatedModel(IEnumerable<AgentResponse> responses) =>
            _responses = new Queue<AgentResponse>(responses);

        public static SimulatedModel Fixed(string text) =>
            new([new AgentResponse { Text = text }]);

        public static SimulatedModel Sequence(params AgentResponse[] responses) =>
            new(responses);

        public int CallCount => _callCount;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_responses.Count > 1 ? _responses.Dequeue() : _responses.Peek());
        }
    }
}
