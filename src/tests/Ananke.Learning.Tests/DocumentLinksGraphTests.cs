using Ananke.Abstractions.Graph;
using Ananke.Learning.Knowledge.Builders;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Learning.Tests;

/// <summary>Links found by pattern are what <see cref="DocumentStructureBuilder"/> already turns into <c>mentions</c> edges.</summary>
[TestFixture]
public class DocumentLinksGraphTests
{
    [Test]
    public async Task AddDocumentAsync_LinksFoundByPattern_BecomeMentionsEdges()
    {
        var text = "Carried out by ZEN-6.";
        var chunk = new KnowledgeDocument
        {
            Id = "plan.md:chunk:0",
            Text = text,
            Metadata = new Dictionary<string, string>(DocumentLinks.Find(
                new Dictionary<string, System.Text.RegularExpressions.Regex> { ["card"] = new(@"\b[A-Z]+-\d+\b") }, text)),
        };
        var graph = new InMemoryKnowledgeGraph();

        await new DocumentStructureBuilder().AddDocumentAsync(graph, chunk);

        var edges = await graph.NeighborsAsync("doc:plan.md:chunk:0");
        edges.ShouldContain(e => e.ToId == "entity:card:ZEN-6" && e.Relation == "mentions" && e.Provenance == EdgeProvenance.Extracted);
    }
}
