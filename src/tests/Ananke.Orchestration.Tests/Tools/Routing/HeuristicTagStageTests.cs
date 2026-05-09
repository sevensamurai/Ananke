using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Tools.Routing;

[TestFixture]
public class HeuristicTagStageTests
{
    [Test]
    public async Task RouteAsync_KeepsMatchingTags()
    {
        var stage = new HeuristicTagStage(_ => new HashSet<string>(["search", "web"]));
        var candidates = new List<ToolMemoryEntry>
        {
            MakeEntry("web_search", ["search", "web"]),
            MakeEntry("calculator", ["math"]),
        };

        var decision = await stage.RouteAsync(MakeRequest("search the web", candidates));

        decision.SelectedTools.ShouldHaveSingleItem();
        decision.SelectedTools[0].ToolName.ShouldBe("web_search");
    }

    [Test]
    public async Task RouteAsync_ReturnsEmpty_WhenNoTagsMatch()
    {
        var stage = new HeuristicTagStage(_ => new HashSet<string>(["finance"]));
        var candidates = new List<ToolMemoryEntry>
        {
            MakeEntry("weather", ["weather", "forecast"]),
        };

        var decision = await stage.RouteAsync(MakeRequest("stock price", candidates));

        decision.SelectedTools.ShouldBeEmpty();
    }

    [Test]
    public async Task RouteAsync_Confidence_IsMedium()
    {
        var stage = new HeuristicTagStage(_ => new HashSet<string>(["x"]));
        var decision = await stage.RouteAsync(MakeRequest("msg", []));
        decision.Confidence.ShouldBe(RoutingConfidence.Medium);
    }

    [Test]
    public async Task RouteAsync_MessagePassedToFunction()
    {
        string? receivedMessage = null;
        var stage = new HeuristicTagStage(msg =>
        {
            receivedMessage = msg;
            return new HashSet<string>();
        });

        await stage.RouteAsync(MakeRequest("test query", []));

        receivedMessage.ShouldBe("test query");
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static ToolRoutingRequest MakeRequest(string msg, IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = msg, Candidates = candidates };

    private static ToolMemoryEntry MakeEntry(string name, IReadOnlyList<string> tags) =>
        new() { ToolName = name, KitName = "kit", Description = $"Desc {name}", Tags = tags };
}
