using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Gating;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Tools.Routing;

[TestFixture]
public class AffinityRerankStageTests
{
    private static ToolAffinityTracker MakeTracker() =>
        new();

    [Test]
    public async Task RouteAsync_UntriedToolsSortFirst()
    {
        var tracker = MakeTracker();
        // Record a known outcome for "known" so it's no longer untried
        tracker.RecordOutcome("kit", "known", reward: 0.5f);

        var stage = new AffinityRerankStage(tracker);
        var candidates = MakeCandidates("known", "untried");

        var decision = await stage.RouteAsync(MakeRequest(candidates));

        // Untried tool should appear first (MaxValue UCB score)
        decision.SelectedTools[0].ToolName.ShouldBe("untried");
    }

    [Test]
    public async Task RouteAsync_HigherReward_RankedFirst()
    {
        var tracker = MakeTracker();
        tracker.RecordOutcome("kit", "good", reward: 1f);
        tracker.RecordOutcome("kit", "bad", reward: -1f);

        var stage = new AffinityRerankStage(tracker);
        var candidates = MakeCandidates("bad", "good");

        var decision = await stage.RouteAsync(MakeRequest(candidates));

        decision.SelectedTools[0].ToolName.ShouldBe("good");
    }

    [Test]
    public async Task RouteAsync_CapsAtMaxSelected()
    {
        var tracker = MakeTracker();
        var stage = new AffinityRerankStage(tracker);
        var request = new ToolRoutingRequest
        {
            UserMessage = "q",
            Candidates = MakeCandidates("a", "b", "c", "d", "e"),
            MaxSelected = 2,
        };

        var decision = await stage.RouteAsync(request);

        decision.SelectedTools.Count.ShouldBe(2);
    }

    [Test]
    public async Task RouteAsync_Confidence_IsMedium()
    {
        var tracker = MakeTracker();
        var stage = new AffinityRerankStage(tracker);
        var decision = await stage.RouteAsync(MakeRequest(MakeCandidates("a")));
        decision.Confidence.ShouldBe(RoutingConfidence.Medium);
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static ToolRoutingRequest MakeRequest(IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = "q", Candidates = candidates };

    private static IReadOnlyList<ToolMemoryEntry> MakeCandidates(params string[] names) =>
        names.Select(n => new ToolMemoryEntry { ToolName = n, KitName = "kit", Description = $"Desc {n}" })
             .ToList();
}
