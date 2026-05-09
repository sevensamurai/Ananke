using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Gating;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Tools.Routing;

[TestFixture]
public class SemanticRecallStageTests
{
    private InMemoryToolMemory _memory = null!;

    [SetUp]
    public void SetUp() => _memory = new InMemoryToolMemory();

    [Test]
    public async Task RouteAsync_RecallsAndIntersectsCandidates()
    {
        await _memory.UpsertAsync(MakeEntry("search", "Searches the web for information"));
        await _memory.UpsertAsync(MakeEntry("calculator", "Performs math calculations"));

        var stage = new SemanticRecallStage(_memory, topK: 5);
        var candidates = MakeCandidates("search", "calculator", "translate");

        var decision = await stage.RouteAsync(MakeRequest("search the web", candidates));

        decision.UseTools.ShouldBeTrue();
        decision.Confidence.ShouldBe(RoutingConfidence.High);
        // "translate" not in memory so should be dropped
        decision.SelectedTools.Any(e => e.ToolName == "translate").ShouldBeFalse();
        decision.SelectedTools.Any(e => e.ToolName == "search").ShouldBeTrue();
    }

    [Test]
    public async Task RouteAsync_ColdStart_EmptyMemory_ReturnsAllCandidates_WithLowConfidence()
    {
        var stage = new SemanticRecallStage(_memory);
        var candidates = MakeCandidates("a", "b", "c");

        var decision = await stage.RouteAsync(MakeRequest("hello", candidates));

        decision.UseTools.ShouldBeTrue();
        decision.Confidence.ShouldBe(RoutingConfidence.Low);
        decision.SelectedTools.Count.ShouldBe(3);
        decision.Rationale.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task RouteAsync_SubsetInvariant_NeverAddsToolsNotInCandidates()
    {
        // Memory has "ghost" which is NOT in candidates
        await _memory.UpsertAsync(MakeEntry("ghost", "A ghost tool"));
        await _memory.UpsertAsync(MakeEntry("real", "A real tool"));

        var stage = new SemanticRecallStage(_memory, topK: 10);
        var candidates = MakeCandidates("real"); // no "ghost"

        var decision = await stage.RouteAsync(MakeRequest("ghost real", candidates));

        decision.SelectedTools.Any(e => e.ToolName == "ghost").ShouldBeFalse();
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static ToolRoutingRequest MakeRequest(string msg, IReadOnlyList<ToolMemoryEntry> candidates) =>
        new() { UserMessage = msg, Candidates = candidates };

    private static IReadOnlyList<ToolMemoryEntry> MakeCandidates(params string[] names) =>
        names.Select(n => new ToolMemoryEntry { ToolName = n, KitName = "kit", Description = $"Desc {n}" })
             .ToList();

    private static ToolMemoryEntry MakeEntry(string name, string description) =>
        new() { ToolName = name, KitName = "kit", Description = description };
}
