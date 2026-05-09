using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Tools.Routing;

[TestFixture]
public class PinnedToolStageTests
{
    [Test]
    public async Task RouteAsync_PinnedToolsMovedToFront()
    {
        var stage = new PinnedToolStage(["help", "list_tools"]);
        var candidates = MakeCandidates("search", "help", "translate", "list_tools");

        var decision = await stage.RouteAsync(MakeRequest(candidates));

        decision.SelectedTools[0].ToolName.ShouldBeOneOf("help", "list_tools");
        decision.SelectedTools[1].ToolName.ShouldBeOneOf("help", "list_tools");
        decision.SelectedTools.Count.ShouldBe(4);
    }

    [Test]
    public async Task RouteAsync_NoPinnedMatchInCandidates_AllToolsReturned()
    {
        var stage = new PinnedToolStage(["nonexistent"]);
        var candidates = MakeCandidates("a", "b");

        var decision = await stage.RouteAsync(MakeRequest(candidates));

        decision.SelectedTools.Count.ShouldBe(2);
    }

    [Test]
    public async Task RouteAsync_Terminal_False_ByDefault()
    {
        var stage = new PinnedToolStage(["help"]);
        var decision = await stage.RouteAsync(MakeRequest(MakeCandidates("help")));
        decision.Terminal.ShouldBeFalse();
    }

    [Test]
    public async Task RouteAsync_Terminal_True_WhenFlagSet()
    {
        var stage = new PinnedToolStage(["help"], terminal: true);
        var decision = await stage.RouteAsync(MakeRequest(MakeCandidates("help")));
        decision.Terminal.ShouldBeTrue();
    }

    [Test]
    public async Task RouteAsync_Confidence_IsHigh()
    {
        var stage = new PinnedToolStage([]);
        var decision = await stage.RouteAsync(MakeRequest(MakeCandidates("a")));
        decision.Confidence.ShouldBe(RoutingConfidence.High);
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static ToolRoutingRequest MakeRequest(IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = "q", Candidates = candidates };

    private static IReadOnlyList<ToolMemoryEntry> MakeCandidates(params string[] names) =>
        names.Select(n => new ToolMemoryEntry { ToolName = n, KitName = "kit", Description = $"Desc {n}" })
             .ToList();
}
