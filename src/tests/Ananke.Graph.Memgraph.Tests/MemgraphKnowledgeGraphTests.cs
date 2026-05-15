using System.Diagnostics;
using Ananke.Abstractions.Graph;
using Ananke.Graph.Abstractions;
using Ananke.Graph.Memgraph;
using Shouldly;

namespace Ananke.Graph.Memgraph.Tests;

/// <summary>
/// Containerised integration tests for <see cref="MemgraphKnowledgeGraph"/>.
/// All tests are skipped automatically when Docker is unavailable or the
/// Memgraph container is not reachable.
/// </summary>
/// <remarks>
/// Start Memgraph before running:
/// <code>docker run -d -p 7687:7687 --name memgraph memgraph/memgraph</code>
/// </remarks>
[TestFixture]
public class MemgraphKnowledgeGraphTests
{
    private MemgraphSessionFactory _factory = null!;
    private MemgraphKnowledgeGraph _graph = null!;

    private static readonly GraphConnectionOptions Options = new()
    {
        Uri      = "bolt://localhost:7687",
        Username = "memgraph",
        Password = string.Empty,
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!IsDockerAvailable())
            Assert.Ignore("Docker is not available — skipping Memgraph integration tests.");

        _factory = new MemgraphSessionFactory(Options);

        try
        {
            await _factory.VerifyConnectivityAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            Assert.Ignore("Memgraph is not reachable on bolt://localhost:7687 — skipping integration tests.");
        }

        await _graph.EnsureSchemaAsync();
    }

    [SetUp]
    public void SetUp()
    {
        _graph = new MemgraphKnowledgeGraph(_factory);
    }

    [OneTimeTearDown]
    public async ValueTask OneTimeTearDown()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }

    // ── Node round-trip ───────────────────────────────────────────────────────

    [Test]
    public async Task UpsertNode_ThenGetNode_ReturnsExpected()
    {
        var node = new GraphNode
        {
            Id   = $"test-{Guid.NewGuid():N}",
            Kind = "entry",
            Properties = new Dictionary<string, string> { ["color"] = "blue" },
        };

        await _graph.UpsertNodeAsync(node);

        var fetched = await _graph.GetNodeAsync(node.Id);
        fetched.ShouldNotBeNull();
        fetched!.Id.ShouldBe(node.Id);
        fetched.Kind.ShouldBe("entry");
        fetched.Properties["color"].ShouldBe("blue");
    }

    [Test]
    public async Task GetNode_UnknownId_ReturnsNull()
    {
        var result = await _graph.GetNodeAsync($"missing-{Guid.NewGuid():N}");
        result.ShouldBeNull();
    }

    // ── Edge round-trip ───────────────────────────────────────────────────────

    [Test]
    public async Task UpsertEdge_ThenNeighbors_ReturnsEdge()
    {
        var a = MakeNode("entry");
        var b = MakeNode("tag");
        await _graph.UpsertNodeAsync(a);
        await _graph.UpsertNodeAsync(b);

        var edge = new GraphEdge
        {
            FromId     = a.Id,
            ToId       = b.Id,
            Relation   = "tagged",
            Provenance = EdgeProvenance.Extracted,
            Weight     = 0.9f,
        };
        await _graph.UpsertEdgeAsync(edge);

        var neighbors = await _graph.NeighborsAsync(a.Id, relation: "tagged");
        neighbors.ShouldNotBeEmpty();
        neighbors[0].Weight.ShouldBeGreaterThan(0f);
    }

    [Test]
    public async Task UpsertEdge_WeightPromotion_KeepsMax()
    {
        var a = MakeNode("entry");
        var b = MakeNode("entry");
        await _graph.UpsertNodeAsync(a);
        await _graph.UpsertNodeAsync(b);

        var edge = new GraphEdge { FromId = a.Id, ToId = b.Id, Relation = "FOLLOWS", Provenance = EdgeProvenance.Inferred, Weight = 0.3f };
        await _graph.UpsertEdgeAsync(edge);
        await _graph.UpsertEdgeAsync(edge with { Weight = 0.8f });
        await _graph.UpsertEdgeAsync(edge with { Weight = 0.5f });

        var neighbors = await _graph.NeighborsAsync(a.Id, relation: "FOLLOWS");
        neighbors.ShouldHaveSingleItem();
        neighbors[0].Weight.ShouldBe(0.8f, tolerance: 0.001f);
    }

    [Test]
    public async Task UpsertEdge_ProvenancePromotion_NeverDemotes()
    {
        var a = MakeNode("entry");
        var b = MakeNode("entry");
        await _graph.UpsertNodeAsync(a);
        await _graph.UpsertNodeAsync(b);

        var edge = new GraphEdge { FromId = a.Id, ToId = b.Id, Relation = "LINKED", Provenance = EdgeProvenance.Extracted, Weight = 1f };
        await _graph.UpsertEdgeAsync(edge);
        // Attempt demotion — should be silently ignored.
        await _graph.UpsertEdgeAsync(edge with { Provenance = EdgeProvenance.Inferred });

        var neighbors = await _graph.NeighborsAsync(a.Id, relation: "LINKED");
        neighbors.ShouldHaveSingleItem();
        neighbors[0].Provenance.ShouldBe(EdgeProvenance.Extracted);
    }

    // ── BFS expansion ─────────────────────────────────────────────────────────

    [Test]
    public async Task ExpandAsync_ReturnsReachableNodes()
    {
        var root  = MakeNode("entry");
        var child = MakeNode("tag");
        var grand = MakeNode("tag");
        await _graph.UpsertNodeAsync(root);
        await _graph.UpsertNodeAsync(child);
        await _graph.UpsertNodeAsync(grand);

        await _graph.UpsertEdgeAsync(new GraphEdge { FromId = root.Id,  ToId = child.Id, Relation = "CHILD", Provenance = EdgeProvenance.Inferred, Weight = 1f });
        await _graph.UpsertEdgeAsync(new GraphEdge { FromId = child.Id, ToId = grand.Id, Relation = "CHILD", Provenance = EdgeProvenance.Inferred, Weight = 1f });

        var expanded = await _graph.ExpandAsync([root.Id], hops: 2, maxNodes: 100);
        var ids = expanded.Select(n => n.Id).ToHashSet();
        ids.ShouldContain(child.Id);
        ids.ShouldContain(grand.Id);
    }

    [Test]
    public async Task ExpandAsync_EmptySeeds_ReturnsEmpty()
    {
        var result = await _graph.ExpandAsync([], hops: 2, maxNodes: 100);
        result.ShouldBeEmpty();
    }

    // ── Counts ────────────────────────────────────────────────────────────────

    [Test]
    public async Task NodeCount_IncreasesAfterUpsert()
    {
        var before = await _graph.NodeCountAsync();
        await _graph.UpsertNodeAsync(MakeNode("entry"));
        var after = await _graph.NodeCountAsync();
        after.ShouldBeGreaterThan(before);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GraphNode MakeNode(string kind) =>
        new() { Id = $"test-{Guid.NewGuid():N}", Kind = kind };

    private static bool IsDockerAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(3_000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
