using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Qdrant.Tests.Containers;

/// <summary><see cref="QdrantKnowledgeCatalog.GetReplacingAsync"/> against Qdrant.</summary>
[TestFixture]
public class QdrantKnowledgeCatalogReplacingTests
{
    private static CatalogEntry Entry(string source, string[] replaces, string? supersededBy = null) => new()
    {
        Source = source,
        Summary = $"Summary of {source}",
        Keywords = [],
        IndexedAt = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero),
        ChunkCount = 1,
        SupersededBy = supersededBy,
        Replaces = replaces,
    };

    [Test]
    public async Task GetReplacingAsync_SeveralEntriesNameTheSource_ReturnsThoseEntries()
    {
        var catalog = new QdrantKnowledgeCatalog(
            QdrantServer.Require(), new InMemoryEmbedder(), QdrantServer.NewCollection("catalog"), vectorSize: 64);
        await catalog.IndexAsync(Entry("7230.md", ["2616.md"]));
        await catalog.IndexAsync(Entry("7231.md", ["2616.md", "notes/\"draft\" 2617.md"], supersededBy: "9110.md"));
        await catalog.IndexAsync(Entry("9110.md", ["7231.md", "old/2616.md", "2616.md.bak"]));

        (await catalog.GetReplacingAsync("2616.md")).Select(e => e.Source).ShouldBe(["7230.md", "7231.md"], ignoreOrder: true);
        (await catalog.GetReplacingAsync("notes/\"draft\" 2617.md")).Select(e => e.Source).ShouldBe(["7231.md"]);
        (await catalog.GetReplacingAsync("2618.md")).ShouldBeEmpty();
    }
}
