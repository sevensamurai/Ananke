using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Tests;
using Shouldly;

#pragma warning disable ANNKEEXP004 // lists through the experimental IListableKnowledgeStore

namespace Ananke.Qdrant.Tests.Containers;

/// <summary><see cref="DottedKeyFilterCases"/> and <see cref="EmptyFilterDeleteCases"/> on <see cref="QdrantKnowledgeStore"/>.</summary>
[TestFixture]
public class QdrantDottedKeyFilterTests
{
    private static QdrantKnowledgeStore NewStore() =>
        new(QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("dotted"), vectorSize: 64);

    [Test]
    public Task List_ADottedKey_MatchesTheDocumentHoldingIt() =>
        DottedKeyFilterCases.List_ADottedKey_MatchesTheDocumentHoldingIt(NewStore());

    [Test]
    public Task Delete_ADottedKey_RemovesTheDocumentHoldingIt() =>
        DottedKeyFilterCases.Delete_ADottedKey_RemovesTheDocumentHoldingIt(NewStore());

    [Test]
    public Task Delete_AnEmptyFilter_ThrowsAndDeletesNothing() =>
        EmptyFilterDeleteCases.Delete_AnEmptyFilter_ThrowsAndDeletesNothing(NewStore());

    [Test]
    public async Task List_ByADateKeyHoldingAColon_OrdersByIt()
    {
        var store = NewStore();
        await store.UpsertAsync(
        [
            new KnowledgeDocument { Id = "old", Text = "old", Metadata = new Dictionary<string, string> { ["meta:date"] = "2026-01-01T00:00:00Z" } },
            new KnowledgeDocument { Id = "new", Text = "new", Metadata = new Dictionary<string, string> { ["meta:date"] = "2026-02-01T00:00:00Z" } },
        ]);

        (await store.ListAsync(new KnowledgeFilter(), order: new KnowledgeDateOrder("meta:date"))).Select(c => c.Id).ShouldBe(["new", "old"]);
    }
}
