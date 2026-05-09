using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Tools.Routing;

[TestFixture]
public class CompositeSmartToolRouterTests
{
    // ── No stages ────────────────────────────────────────────────────

    [Test]
    public async Task Composite_NoStages_BehavesLikePassThrough()
    {
        var router = new CompositeSmartToolRouter([]);
        var candidates = MakeCandidates("a", "b", "c");
        var request = MakeRequest("hello", candidates);

        var decision = await router.RouteAsync(request);

        decision.UseTools.ShouldBeTrue();
        decision.SelectedTools.ShouldBe(candidates);
        decision.Confidence.ShouldBe(RoutingConfidence.High);
    }

    // ── Chaining ─────────────────────────────────────────────────────

    [Test]
    public async Task Composite_ChainsCandidatesFromPreviousDecision()
    {
        // Stage 1 drops "c"; stage 2 should only see "a" and "b"
        IReadOnlyList<ToolMemoryEntry>? stage2Received = null;
        var stage1 = new FuncRouter(req =>
            new ToolRoutingDecision
            {
                UseTools = true,
                Confidence = RoutingConfidence.High,
                SelectedTools = req.Candidates.Where(e => e.ToolName != "c").ToList(),
            });
        var stage2 = new FuncRouter(req =>
        {
            stage2Received = req.Candidates;
            return new ToolRoutingDecision
            {
                UseTools = true,
                Confidence = RoutingConfidence.High,
                SelectedTools = req.Candidates,
            };
        });

        var router = new CompositeSmartToolRouter([stage1, stage2]);
        await router.RouteAsync(MakeRequest("q", MakeCandidates("a", "b", "c")));

        stage2Received.ShouldNotBeNull();
        stage2Received!.Select(e => e.ToolName).ShouldBe(["a", "b"], ignoreOrder: true);
    }

    // ── Terminal ─────────────────────────────────────────────────────

    [Test]
    public async Task Composite_TerminalStops()
    {
        var stage1 = new FuncRouter(req => new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
            SelectedTools = req.Candidates.Take(1).ToList(),
            Terminal = true,
        });
        var stage2WasCalled = false;
        var stage2 = new FuncRouter(_ =>
        {
            stage2WasCalled = true;
            return new ToolRoutingDecision { UseTools = true, Confidence = RoutingConfidence.High };
        });

        var router = new CompositeSmartToolRouter([stage1, stage2]);
        var decision = await router.RouteAsync(MakeRequest("q", MakeCandidates("a", "b")));

        stage2WasCalled.ShouldBeFalse();
        decision.SelectedTools.Count.ShouldBe(1);
    }

    // ── Low-confidence escalation ─────────────────────────────────────

    [Test]
    public async Task Composite_LowConfidenceDoesNotNarrow()
    {
        IReadOnlyList<ToolMemoryEntry>? stage2Received = null;
        var stage1 = new FuncRouter(req => new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.Low,
            // Stage 1 tries to narrow to just "a"
            SelectedTools = req.Candidates.Take(1).ToList(),
        });
        var stage2 = new FuncRouter(req =>
        {
            stage2Received = req.Candidates;
            return new ToolRoutingDecision
            {
                UseTools = true,
                Confidence = RoutingConfidence.High,
                SelectedTools = req.Candidates,
            };
        });

        var router = new CompositeSmartToolRouter([stage1, stage2]);
        await router.RouteAsync(MakeRequest("q", MakeCandidates("a", "b", "c")));

        // Stage 2 should still see all three (escalation — Low confidence did not narrow)
        stage2Received!.Count.ShouldBe(3);
    }

    // ── Subset invariant ─────────────────────────────────────────────

    [Test]
    public async Task Composite_RejectsAddedTools_ThrowsInvalidRoutingDecisionException()
    {
        var intruder = new ToolMemoryEntry { ToolName = "injected", KitName = "other", Description = "x" };
        var stage = new FuncRouter(req => new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
            SelectedTools = [.. req.Candidates, intruder],
        });

        var router = new CompositeSmartToolRouter([stage]);
        await Should.ThrowAsync<InvalidRoutingDecisionException>(
            () => router.RouteAsync(MakeRequest("q", MakeCandidates("a", "b"))));
    }

    // ── High-confidence UseTools=false short-circuit ─────────────────

    [Test]
    public async Task Composite_HighConfidenceUseToolsFalse_ShortCircuits()
    {
        var stage1 = new FuncRouter(_ => new ToolRoutingDecision
        {
            UseTools = false,
            Confidence = RoutingConfidence.High,
        });
        var stage2WasCalled = false;
        var stage2 = new FuncRouter(_ =>
        {
            stage2WasCalled = true;
            return new ToolRoutingDecision { UseTools = true, Confidence = RoutingConfidence.High };
        });

        var router = new CompositeSmartToolRouter([stage1, stage2]);
        var decision = await router.RouteAsync(MakeRequest("q", MakeCandidates("a")));

        stage2WasCalled.ShouldBeFalse();
        decision.UseTools.ShouldBeFalse();
    }

    // ── MaxSelected clamp ─────────────────────────────────────────────

    [Test]
    public async Task Composite_ClampsToMaxSelected()
    {
        var stage = new FuncRouter(req => new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
            SelectedTools = req.Candidates,
        });
        var router = new CompositeSmartToolRouter([stage]);
        var request = new ToolRoutingRequest
        {
            UserMessage = "q",
            Candidates = MakeCandidates("a", "b", "c", "d", "e"),
            MaxSelected = 3,
        };

        var decision = await router.RouteAsync(request);

        decision.SelectedTools.Count.ShouldBe(3);
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static ToolRoutingRequest MakeRequest(string msg, IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = msg, Candidates = candidates };

    private static IReadOnlyList<ToolMemoryEntry> MakeCandidates(params string[] names) =>
        names.Select(n => new ToolMemoryEntry { ToolName = n, KitName = "kit", Description = $"Desc {n}" })
             .ToList();

    private sealed class FuncRouter(Func<ToolRoutingRequest, ToolRoutingDecision> fn) : ISmartToolRouter
    {
        public Task<ToolRoutingDecision> RouteAsync(ToolRoutingRequest request, CancellationToken ct = default)
            => Task.FromResult(fn(request));
    }
}
