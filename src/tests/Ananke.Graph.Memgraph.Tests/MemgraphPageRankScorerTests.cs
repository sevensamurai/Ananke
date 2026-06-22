using System.Diagnostics;
using Ananke.Abstractions.Graph;
using Ananke.Graph.Abstractions;
using Ananke.Graph.Memgraph;
using Shouldly;

namespace Ananke.Graph.Memgraph.Tests;

/// <summary>
/// Containerised integration tests for <see cref="MemgraphPageRankScorer"/>.
/// All tests are skipped automatically when Docker is unavailable, the Memgraph container is
/// not reachable, or MAGE is not installed.
/// </summary>
/// <remarks>
/// Start Memgraph with MAGE before running:
/// <code>docker run -d -p 7687:7687 --name memgraph memgraph/memgraph-mage</code>
/// </remarks>
[TestFixture]
public class MemgraphPageRankScorerTests
{
    private MemgraphSessionFactory _factory = null!;
    private MemgraphKnowledgeGraph _graph = null!;
    private MemgraphPageRankScorer _scorer = null!;

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
    }

    [SetUp]
    public void SetUp()
    {
        _graph  = new MemgraphKnowledgeGraph(_factory);
        _scorer = new MemgraphPageRankScorer(_factory);
    }

    [OneTimeTearDown]
    public async ValueTask OneTimeTearDown()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }

    [Test]
    public async Task ScoreAsync_NodeKindFilterMatchesSecondaryLabel_IncludesNode()
    {
        var a = MakeNode("Service", "Component");
        var b = MakeNode("Service", "Component");
        var other = MakeNode("Entity");

        await _graph.UpsertNodeAsync(a);
        await _graph.UpsertNodeAsync(b);
        await _graph.UpsertNodeAsync(other);
        await _graph.UpsertEdgeAsync(new GraphEdge
        {
            FromId = a.Id, ToId = b.Id, Relation = "CALLS", Provenance = EdgeProvenance.Extracted,
        });

        var scores = await _scorer.ScoreAsync(_graph, nodeKindFilter: "Component");

        scores.ContainsKey(a.Id).ShouldBeTrue();
        scores.ContainsKey(b.Id).ShouldBeTrue();
        scores.ContainsKey(other.Id).ShouldBeFalse();
    }

    private static GraphNode MakeNode(string kind, params string[] labels) =>
        new() { Id = $"test-{Guid.NewGuid():N}", Kind = kind, Labels = labels };

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
