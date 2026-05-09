using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Tools.Routing;

[TestFixture]
public class HealthFilterStageTests
{
    private static readonly HealthFilterStage Stage = new();

    [Test]
    public async Task RouteAsync_DropsOfflineTools()
    {
        var candidates = new List<ToolMemoryEntry>
        {
            MakeEntry("good", ToolHealth.Healthy),
            MakeEntry("bad", ToolHealth.Offline),
        };

        var decision = await Stage.RouteAsync(MakeRequest(candidates));

        decision.SelectedTools.ShouldHaveSingleItem();
        decision.SelectedTools[0].ToolName.ShouldBe("good");
    }

    [Test]
    public async Task RouteAsync_DropsCooldownTools()
    {
        var candidates = new List<ToolMemoryEntry>
        {
            MakeEntry("ok", ToolHealth.Degraded),
            MakeEntry("cooldown", ToolHealth.Cooldown),
        };

        var decision = await Stage.RouteAsync(MakeRequest(candidates));

        decision.SelectedTools.ShouldHaveSingleItem();
        decision.SelectedTools[0].ToolName.ShouldBe("ok");
    }

    [Test]
    public async Task RouteAsync_KeepsDegradedTools()
    {
        var candidates = new List<ToolMemoryEntry>
        {
            MakeEntry("degraded", ToolHealth.Degraded),
        };

        var decision = await Stage.RouteAsync(MakeRequest(candidates));

        decision.SelectedTools.ShouldHaveSingleItem();
    }

    [Test]
    public async Task RouteAsync_Confidence_IsHigh()
    {
        var decision = await Stage.RouteAsync(MakeRequest([]));
        decision.Confidence.ShouldBe(RoutingConfidence.High);
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static ToolRoutingRequest MakeRequest(IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = "q", Candidates = candidates };

    private static ToolMemoryEntry MakeEntry(string name, ToolHealth health) =>
        new() { ToolName = name, KitName = "kit", Description = $"Desc {name}", Health = health };
}
