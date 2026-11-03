using Ananke.Abstractions.Graph;
using Ananke.Learning.Knowledge.Builders;
using Ananke.Orchestration.Knowledge;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Learning.Tests.Knowledge;

[TestFixture]
public sealed class DocumentStructureBuilderTests
{
    [Test]
    public async Task AddDocumentAsync_StampsEveryEdgeFromItsClock()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var graph = new InMemoryKnowledgeGraph();
        var document = new KnowledgeDocument
        {
            Id = "d1",
            Text = "release notes",
            Metadata = new Dictionary<string, string> { ["entity:1"] = "ananke", ["cites:1"] = "d0" },
        };

        await new DocumentStructureBuilder(new FakeTimeProvider(now)).AddDocumentAsync(graph, document);

        var edges = await graph.NeighborsAsync("doc:d1");
        edges.Select(e => e.Relation).ShouldBe(["mentions", "cites"], ignoreOrder: true);
        edges.ShouldAllBe(e => e.ObservedAt == now);
    }
}
