using Ananke.Abstractions.Graph;
using Ananke.Learning.Episodes;
using Ananke.Learning.Knowledge.Builders;
using Shouldly;

namespace Ananke.Learning.Tests.Knowledge;

[TestFixture]
public sealed class EpisodeTrajectoryBuilderTests
{
    private InMemoryKnowledgeGraph _graph = null!;
    private InMemoryEpisodeStore   _store = null!;
    private EpisodeTrajectoryBuilder _builder = null!;

    [SetUp]
    public void SetUp()
    {
        _graph   = new InMemoryKnowledgeGraph();
        _store   = new InMemoryEpisodeStore();
        _builder = new EpisodeTrajectoryBuilder(_store);
    }

    [Test]
    public async Task BuildAsync_Episode_ProducesEntryAndEpisodeNodes()
    {
        await _store.CommitAsync(MakeEpisode("ep1", ["e1", "e2", "e3"]));

        await _builder.BuildAsync(_graph);

        (await _graph.GetNodeAsync("entry:e1")).ShouldNotBeNull();
        (await _graph.GetNodeAsync("entry:e2")).ShouldNotBeNull();
        (await _graph.GetNodeAsync("entry:e3")).ShouldNotBeNull();
        (await _graph.GetNodeAsync("episode:ep1")).ShouldNotBeNull();
    }

    [Test]
    public async Task BuildAsync_Episode_ProducesNMinusOneFollowsEdges()
    {
        // 4-step episode → 3 follows edges.
        await _store.CommitAsync(MakeEpisode("ep1", ["e1", "e2", "e3", "e4"]));

        await _builder.BuildAsync(_graph);

        var followsFromE1 = (await _graph.NeighborsAsync("entry:e1", relation: "follows"))
            .Where(e => e.FromId == "entry:e1").ToList();
        followsFromE1.Count.ShouldBe(1);
        followsFromE1[0].ToId.ShouldBe("entry:e2");

        var followsFromE3 = (await _graph.NeighborsAsync("entry:e3", relation: "follows"))
            .Where(e => e.FromId == "entry:e3").ToList();
        followsFromE3.Count.ShouldBe(1);
        followsFromE3[0].ToId.ShouldBe("entry:e4");
    }

    [Test]
    public async Task BuildAsync_Episode_EveryEntryHasStepOfEdge()
    {
        await _store.CommitAsync(MakeEpisode("ep1", ["e1", "e2"]));

        await _builder.BuildAsync(_graph);

        var e1Steps = await _graph.NeighborsAsync("entry:e1", relation: "step_of");
        var e2Steps = await _graph.NeighborsAsync("entry:e2", relation: "step_of");

        e1Steps.ShouldContain(e => e.ToId == "episode:ep1");
        e2Steps.ShouldContain(e => e.ToId == "episode:ep1");
    }

    [Test]
    public async Task BuildAsync_SingleStepEpisode_ProducesNoFollowsEdges()
    {
        await _store.CommitAsync(MakeEpisode("ep1", ["e1"]));

        await _builder.BuildAsync(_graph);

        var follows = await _graph.NeighborsAsync("entry:e1", relation: "follows");
        follows.Count.ShouldBe(0);
    }

    [Test]
    public async Task BuildAsync_FollowsEdges_AreExtracted()
    {
        await _store.CommitAsync(MakeEpisode("ep1", ["e1", "e2"]));

        await _builder.BuildAsync(_graph);

        var edge = (await _graph.NeighborsAsync("entry:e1", relation: "follows"))[0];
        edge.Provenance.ShouldBe(EdgeProvenance.Extracted);
    }

    // ── helper ──────────────────────────────────────────────────────────────

    private static Episode MakeEpisode(string id, string[] entryIds) => new()
    {
        Id             = id,
        TerminalReward = 1f,
        StartedAt      = DateTimeOffset.UtcNow,
        CompletedAt    = DateTimeOffset.UtcNow,
        Steps          = entryIds.Select((eid, i) => new EpisodeStep
        {
            StepIndex = i,
            EntryId   = eid,
        }).ToList(),
    };
}
