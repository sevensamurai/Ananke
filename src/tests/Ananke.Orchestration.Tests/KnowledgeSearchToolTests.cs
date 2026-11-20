using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary><see cref="KnowledgeSearchTool"/>'s results as the model reads them.</summary>
[TestFixture]
public class KnowledgeSearchToolTests
{
    private static KnowledgeChunk Chunk(Dictionary<string, string> metadata) =>
        new() { Id = "rfc7231:chunk:0", Text = "HTTP/1.1 semantics", Score = 0.9f, Metadata = metadata };

    [Test]
    public void FormatResults_ADatedSupersededResult_SaysItsDateAndWhatSupersedesIt()
    {
        var text = KnowledgeSearchTool.FormatResults([Chunk(new()
        {
            ["source"] = "rfc7231",
            ["effective_at"] = "2014-06-01T00:00:00.0000000+00:00",
            ["superseded_by"] = "rfc9110",
        })]);

        text.ShouldContain("Source: rfc7231\nDate: 2014-06-01\nSuperseded by: rfc9110\nHTTP/1.1 semantics", Case.Sensitive);
    }

    [Test]
    public void FormatResults_NeitherInTheMetadata_SaysNeither()
    {
        var text = KnowledgeSearchTool.FormatResults([Chunk(new() { ["source"] = "rfc9110" })]);

        text.ShouldNotContain("Date:");
        text.ShouldNotContain("Superseded by:");
    }

    [Test]
    public void FormatResults_ADateThatDoesNotParse_PrintsItAsItIs()
    {
        var text = KnowledgeSearchTool.FormatResults([Chunk(new() { ["effective_at"] = "before the second draft" })]);

        text.ShouldContain("Date: before the second draft");
    }

    [Test]
    public async Task SearchKnowledge_InKnowledgeTools_SaysWhatSupersedesAResult()
    {
        var store = new StubStore([Chunk(new() { ["source"] = "rfc7231", ["superseded_by"] = "rfc9110" })]);
        using var http = new HttpClient();
        var processor = new DocumentProcessor(http, [], new SlidingWindowChunker(), store);

        var result = await KnowledgeTools.Create(processor, store).Tools["search_knowledge"]
            .ExecuteAsync(new Dictionary<string, object?> { ["query"] = "HTTP semantics" });

        result.Value.ShouldContain("Superseded by: rfc9110");
    }

    private sealed class StubStore(IReadOnlyList<KnowledgeChunk> results) : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, SearchOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult(results);

        public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default) => Task.CompletedTask;
    }
}
