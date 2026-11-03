using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

#pragma warning disable ANNKEEXP004

namespace Ananke.Orchestration.Knowledge.Tests.Catalog;

/// <summary>
/// A catalog entry keeps the header fields ingestion kept, and browsing lists documents by a field's value, newest
/// first by their own date: "the status of every plan" as a list.
/// </summary>
[TestFixture]
public class CatalogFieldsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private InMemoryKnowledgeCatalog _catalog = null!;

    [SetUp]
    public void SetUp() => _catalog = new InMemoryKnowledgeCatalog(new InMemoryEmbedder());

    internal static CatalogEntry Entry(string source, string type, string status, DateTimeOffset indexedAt, DateTimeOffset? effectiveAt = null) => new()
    {
        Source = source,
        Summary = "",
        Keywords = [],
        IndexedAt = indexedAt,
        EffectiveAt = effectiveAt,
        ChunkCount = 1,
        Fields = new Dictionary<string, string> { ["Type"] = type, ["Status"] = status },
    };

    [Test]
    public async Task UpsertAsync_ChunksMarkingHeaderFields_KeepsThoseOnTheEntry()
    {
        var store = new CatalogAwareKnowledgeStore(new InMemoryKnowledgeStore(new InMemoryEmbedder()), _catalog);

        await store.UpsertAsync([new KnowledgeDocument
        {
            Id = "plan-a.md:chunk:0",
            Text = "[Status: Accepted · Type: plan]\nThe plan",
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "plan-a.md",
                ["chunk_index"] = "0",
                ["Status"] = "Accepted",
                ["Type"] = "plan",
                [DocumentHeaderOptions.HeaderFieldsKey] = """["Status","Type"]""",
            },
        }]);

        var entry = (await _catalog.GetAsync("plan-a.md"))!;
        entry.Fields.ShouldBe(new Dictionary<string, string> { ["Status"] = "Accepted", ["Type"] = "plan" }, ignoreOrder: true);
    }

    [Test]
    public async Task BrowseAsync_AFieldAndItsValue_ListsTheEntriesWithIt()
    {
        await _catalog.IndexAsync(Entry("plan-a.md", "plan", "Accepted", Now));
        await _catalog.IndexAsync(Entry("plan-b.md", "plan", "Closed", Now.AddDays(-1)));
        await _catalog.IndexAsync(Entry("adr-1.md", "adr", "Accepted", Now.AddDays(-2)));

        var plans = await _catalog.BrowseAsync(new CatalogBrowseOptions { Field = "type", Value = "PLAN" });

        plans.Select(e => e.Source).ShouldBe(["plan-a.md", "plan-b.md"]);
    }

    [Test]
    public async Task BrowseAsync_OrderedByEffectiveAt_NewestOwnDateFirstElseIndexedAt()
    {
        await _catalog.IndexAsync(Entry("old.md", "plan", "Closed", Now, effectiveAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await _catalog.IndexAsync(Entry("new.md", "plan", "Open", Now.AddDays(-2), effectiveAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        await _catalog.IndexAsync(Entry("undated.md", "plan", "Open", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));

        var listed = await _catalog.BrowseAsync(new CatalogBrowseOptions { Order = CatalogBrowseOrder.EffectiveAt });

        listed.Select(e => e.Source).ShouldBe(["new.md", "undated.md", "old.md"]);
    }
}
