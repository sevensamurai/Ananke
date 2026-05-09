using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Tools.Routing;

[TestFixture]
public class PassThroughRouterTests
{
    [Test]
    public async Task RouteAsync_ReturnsAllCandidates_WithHighConfidence()
    {
        var candidates = MakeCandidates("tool_a", "tool_b", "tool_c");
        var request = MakeRequest("hello", candidates);

        var decision = await PassThroughRouter.Instance.RouteAsync(request);

        decision.UseTools.ShouldBeTrue();
        decision.Confidence.ShouldBe(RoutingConfidence.High);
        decision.SelectedTools.ShouldBe(candidates);
    }

    [Test]
    public async Task RouteAsync_EmptyCandidates_ReturnsEmpty()
    {
        var request = MakeRequest("hello", []);

        var decision = await PassThroughRouter.Instance.RouteAsync(request);

        decision.UseTools.ShouldBeTrue();
        decision.SelectedTools.ShouldBeEmpty();
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static ToolRoutingRequest MakeRequest(string msg, IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = msg, Candidates = candidates };

    private static IReadOnlyList<ToolMemoryEntry> MakeCandidates(params string[] names) =>
        names.Select(n => new ToolMemoryEntry { ToolName = n, KitName = "kit", Description = $"Desc {n}" })
             .ToList();
}
