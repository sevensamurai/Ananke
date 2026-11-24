using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Qdrant.Tests.Containers;

/// <summary>
/// <see cref="QdrantKnowledgeCatalog.BrowseAsync"/> orders in the query, so with more entries than the limit it returns
/// the newest, not the first by point id. Entry <c>doc-n</c> was indexed <c>n</c> hours after the first; every third has
/// its own date, three hours after it was indexed. <see cref="QdrantKnowledgeCatalog.GetManyAsync"/> reads from the same
/// entries.
/// </summary>
[TestFixture]
public class QdrantKnowledgeCatalogBrowseTests
{
    private const int Count = 300;

    private static readonly DateTimeOffset First = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private QdrantKnowledgeCatalog? _catalog;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        if (QdrantServer.Client is not { } client)
            return;

        var collection = QdrantServer.NewCollection("catalog");
        var writer = new QdrantKnowledgeCatalog(client, new InMemoryEmbedder(), collection, vectorSize: 64);
        for (var i = 0; i < Count; i++)
        {
            await writer.IndexAsync(new CatalogEntry
            {
                Source = $"doc-{i:D3}",
                Summary = $"Document {i}",
                Keywords = [],
                Category = "notes",
                IndexedAt = First.AddHours(i),
                EffectiveAt = i % 3 == 0 ? First.AddHours(i + 3) : null,
                ChunkCount = 1,
            });
        }

        // A second catalog over the collection the first made, as a process that starts later opens it.
        _catalog = new QdrantKnowledgeCatalog(client, new InMemoryEmbedder(), collection, vectorSize: 64);
    }

    private QdrantKnowledgeCatalog Catalog()
    {
        QdrantServer.Require();
        return _catalog!;
    }

    [Test]
    public async Task BrowseAsync_ByIndexedAtWithMoreThanTheLimit_ReturnsTheNewest()
    {
        var entries = await Catalog().BrowseAsync(new CatalogBrowseOptions { Limit = 5 });

        entries.Select(e => e.Source).ShouldBe(["doc-299", "doc-298", "doc-297", "doc-296", "doc-295"]);
    }

    [Test]
    public async Task BrowseAsync_ByEffectiveAtWithMoreThanTheLimit_ReturnsTheNewestByOwnDateElseIndexedAt()
    {
        var entries = await Catalog().BrowseAsync(new CatalogBrowseOptions { Limit = 5, Order = CatalogBrowseOrder.EffectiveAt });

        entries.Select(e => e.Source).ShouldBe(["doc-297", "doc-299", "doc-298", "doc-294", "doc-296"]);
    }

    [Test]
    public async Task GetManyAsync_SeveralSources_ReturnsTheEntriesFoundAndLeavesOutTheMissing()
    {
        var entries = await Catalog().GetManyAsync(["doc-150", "missing", "doc-003", "doc-150"]);

        entries.Select(e => e.Source).ShouldBe(["doc-003", "doc-150"], ignoreOrder: true);
        entries.Single(e => e.Source == "doc-003").EffectiveAt.ShouldBe(First.AddHours(6));
        (await Catalog().GetManyAsync([])).ShouldBeEmpty();
    }
}
