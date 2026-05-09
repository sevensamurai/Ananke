using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Faults;
using Ananke.Orchestration.Tools.Gating;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ToolAffinityTrackerTests
{
    [Test]
    public async Task ReportAsync_FaultPenalty_ReducesMeanReward()
    {
        var tracker = new ToolAffinityTracker(faultPenalty: -1f);
        tracker.RecordOutcome("agent", "bad_tool", reward: 0f);
        await tracker.ReportAsync(new ToolFaultEvent("agent", "bad_tool", "timeout",
            ContractBreak: false, Transient: true));
        var affinities = tracker.GetAffinities();
        affinities.TryGetValue("agent::bad_tool", out var aff).ShouldBeTrue();
        aff.MeanReward.ShouldBeLessThan(0f);
    }

    [Test]
    public void RecordOutcome_PositiveRewards_IncreaseMeanReward()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("kit", "tool_a", reward: 1f);
        tracker.RecordOutcome("kit", "tool_a", reward: 0.8f);
        var affinities = tracker.GetAffinities();
        affinities["kit::tool_a"].MeanReward.ShouldBeGreaterThan(0f);
    }

    [Test]
    public async Task ToolAffinityTracker_ImplementsIToolFaultObserver()
    {
        var tracker = new ToolAffinityTracker();
        IToolFaultObserver observer = tracker;
        await Should.NotThrowAsync(() =>
            observer.ReportAsync(new ToolFaultEvent("agent", "tool_x", "err",
                ContractBreak: false, Transient: true)).AsTask());
    }

    [Test]
    public async Task AffinityRerank_UntriedToolsSortFirst()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("kit", "known", reward: 0.5f);
        var stage = new AffinityRerankStage(tracker);
        var candidates = MakeCandidates("known", "untried");
        var decision = await stage.RouteAsync(MakeRequest(candidates));
        decision.SelectedTools[0].ToolName.ShouldBe("untried");
    }

    [Test]
    public async Task AffinityRerank_HigherReward_RanksFirst()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("kit", "good", reward: 1f);
        tracker.RecordOutcome("kit", "bad", reward: -1f);
        var stage = new AffinityRerankStage(tracker);
        var candidates = MakeCandidates("bad", "good");
        var decision = await stage.RouteAsync(MakeRequest(candidates));
        decision.SelectedTools[0].ToolName.ShouldBe("good");
    }

    private static ToolRoutingRequest MakeRequest(IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = "q", Candidates = candidates };

    private static IReadOnlyList<ToolMemoryEntry> MakeCandidates(params string[] names) =>
        names.Select(n => new ToolMemoryEntry { ToolName = n, KitName = "kit", Description = $"Desc {n}" })
             .ToList();
}

[TestFixture]
public class ToolPrunerTests
{
    private static async Task<(ToolKit Kit, InMemoryToolMemory Memory)> BuildKitAsync(
        params (string Name, ToolHealth Health, int HitCount, DateTimeOffset LastUsed)[] tools)
    {
        var memory = new InMemoryToolMemory();
        var kit = new ToolKit("agent").WithMemory(memory);
        foreach (var (name, _, _, _) in tools)
            kit.AddTool(name, $"Tool {name}", () => ToolResult.Ok("ok"));
        await kit.PopulateMemoryAsync();
        foreach (var (name, health, hitCount, lastUsed) in tools)
            await memory.UpsertAsync(new ToolMemoryEntry
            {
                KitName = "agent",
                ToolName = name,
                Description = $"Tool {name}",
                Health = health,
                HitCount = hitCount,
                LastUsed = lastUsed,
            });
        return (kit, memory);
    }

    [Test]
    public async Task TickAsync_IdleLowHitTool_IsPruned()
    {
        var staleDate = DateTimeOffset.UtcNow - TimeSpan.FromDays(2);
        var (kit, memory) = await BuildKitAsync(
            ("idle_tool", ToolHealth.Healthy, HitCount: 0, LastUsed: staleDate));
        var pruner = new ToolPruner(memory, kit) { IdleTtl = TimeSpan.FromHours(1), MinHitCount = 3 };
        await pruner.TickAsync();
        var recalled = await memory.RecallAsync("idle tool", topK: 5);
        recalled.Any(e => e.ToolName == "idle_tool").ShouldBeFalse();
    }

    [Test]
    public async Task TickAsync_ActiveTool_NotPruned()
    {
        var (kit, memory) = await BuildKitAsync(
            ("active_tool", ToolHealth.Healthy, HitCount: 10, LastUsed: DateTimeOffset.UtcNow));
        var pruner = new ToolPruner(memory, kit) { IdleTtl = TimeSpan.FromHours(1), MinHitCount = 3 };
        await pruner.TickAsync();
        var recalled = await memory.RecallAsync("active tool", topK: 5);
        recalled.Any(e => e.ToolName == "active_tool").ShouldBeTrue();
    }

    [Test]
    public async Task TickAsync_OfflineTool_AlwaysPruned()
    {
        var (kit, memory) = await BuildKitAsync(
            ("broken_tool", ToolHealth.Offline, HitCount: 50, LastUsed: DateTimeOffset.UtcNow));
        var pruner = new ToolPruner(memory, kit) { IdleTtl = TimeSpan.FromDays(999), MinHitCount = 0 };
        await pruner.TickAsync();
        var recalled = await memory.RecallAsync("broken tool", topK: 5);
        recalled.Any(e => e.ToolName == "broken_tool").ShouldBeFalse();
    }

    [Test]
    public async Task TickAsync_PinnedTool_NotPruned()
    {
        var staleDate = DateTimeOffset.UtcNow - TimeSpan.FromDays(30);
        var (kit, memory) = await BuildKitAsync(
            ("pinned_tool", ToolHealth.Healthy, HitCount: 0, LastUsed: staleDate));
        kit.PinTool("pinned_tool");
        var pruner = new ToolPruner(memory, kit) { IdleTtl = TimeSpan.FromHours(1), MinHitCount = 3 };
        await pruner.TickAsync();
        var recalled = await memory.RecallAsync("pinned tool", topK: 5);
        recalled.Any(e => e.ToolName == "pinned_tool").ShouldBeTrue();
    }

    [Test]
    public async Task TickAsync_IdleButHighHitCount_NotPruned()
    {
        var staleDate = DateTimeOffset.UtcNow - TimeSpan.FromDays(2);
        var (kit, memory) = await BuildKitAsync(
            ("popular_tool", ToolHealth.Healthy, HitCount: 100, LastUsed: staleDate));
        var pruner = new ToolPruner(memory, kit) { IdleTtl = TimeSpan.FromHours(1), MinHitCount = 50 };
        await pruner.TickAsync();
        var recalled = await memory.RecallAsync("popular tool", topK: 5);
        recalled.Any(e => e.ToolName == "popular_tool").ShouldBeTrue();
    }
}
