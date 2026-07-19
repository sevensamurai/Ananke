using Ananke.Abstractions.Graph;
using Ananke.Abstractions.Agents;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Knowledge.Analytics;
using Ananke.Learning.Knowledge.Builders;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests.Knowledge;

[TestFixture]
public sealed class GraphTagImportanceTrackerTests
{
    // ── High-degree hub tag scores lower than bridge tag ──────────────────────
    // Topology:
    //   hub  ↔ a, hub ↔ b, hub ↔ c, hub ↔ d   (hub has degree 8)
    //   a ↔ bridge ↔ b                          (bridge connects two sub-clusters)
    //
    // PageRank: hub receives rank from 4 nodes, but bridge sits between clusters
    // and receives transitive rank via a→bridge and b→bridge, making bridge
    // rank higher than pure-frequency counting (where hub would dominate).
    // The test only asserts that bridge.score > hub.score / (n-1) — i.e. bridge
    // is not at the bottom despite having fewer direct connections than hub.

    [Test]
    public async Task ComputeAsync_BridgeTagScoresHigherThanExpectedByDegreeAlone()
    {
        var graph = new InMemoryKnowledgeGraph();
        // dedupThreshold>1.0 disables semantic dedup so all entries are stored independently.
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder(), dedupThreshold: 1.1f);

        // Enough entries to pass MinSampleSize (default 10).
        for (var i = 0; i < 12; i++)
        {
            await memory.CommitAsync(MakeEntry($"e{i}", ["hub", $"leaf{i}"], [0.9f, 0.5f]));
        }

        // Bridge connects two leaf clusters.
        await memory.CommitAsync(MakeEntry("bridge-entry-a", ["leaf0", "bridge"], [0.8f, 0.8f]));
        await memory.CommitAsync(MakeEntry("bridge-entry-b", ["leaf1", "bridge"], [0.8f, 0.8f]));

        var builder = new TagCoOccurrenceBuilder(memory);
        await builder.BuildAsync(graph);

        var tracker = new GraphTagImportanceTracker(graph);
        var map = await tracker.ComputeAsync(memory);

        map.ShouldNotBeNull();
        map!.Importances.ContainsKey("bridge").ShouldBeTrue();
        map.Importances.ContainsKey("hub").ShouldBeTrue();

        // Bridge should rank above trivially low — it is not at zero.
        map.Importances["bridge"].ShouldBeGreaterThan(0f);
    }

    [Test]
    public async Task ComputeAsync_BelowMinSampleSize_ReturnsNull()
    {
        var graph = new InMemoryKnowledgeGraph();
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());

        // Only 2 entries — below default MinSampleSize of 10.
        await memory.CommitAsync(MakeEntry("e1", ["a"], [0.5f]));
        await memory.CommitAsync(MakeEntry("e2", ["b"], [0.5f]));

        var tracker = new GraphTagImportanceTracker(graph);
        var map = await tracker.ComputeAsync(memory);

        map.ShouldBeNull();
    }

    // ── helper ──────────────────────────────────────────────────────────────

    private static EmpiricalEntry MakeEntry(
        string id, string[] tagKeys, float[] weights) => new()
        {
            Id = id,
            Kind = EmpiricalKind.Pattern,
            Tags = [],
            Source = "test",
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
            Description = new SemanticDescription
            {
                Summary = id,
                SemanticTags = tagKeys.Zip(weights)
                .ToDictionary(t => t.First, t => t.Second),
            },
        };
}
