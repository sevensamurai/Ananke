using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Catalog;
using Shouldly;

namespace Ananke.Postgres.Tests;

/// <summary><see cref="PostgresKnowledgeCatalog"/> against Postgres with pgvector.</summary>
[TestFixture]
public class PostgresKnowledgeCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private PostgresKnowledgeCatalog _catalog = null!;

    [SetUp]
    public void SetUp() => _catalog = new PostgresKnowledgeCatalog(
        PostgresDatabase.Require(),
        new FixedEmbedder(new Dictionary<string, float[]>(), new EmbeddingModelIdentity("test", "fixed", 3)),
        new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("catalog") });

    private static CatalogEntry Entry(string source, string category = "design", int daysOld = 0, string? supersededBy = null) => new()
    {
        Source = source,
        Summary = $"Summary of {source}",
        Keywords = ["identity", source],
        Category = category,
        IndexedAt = Now.AddDays(-daysOld),
        ChunkCount = 3,
        SupersededBy = supersededBy,
    };

    [Test]
    public async Task IndexAsync_ThenGetAsync_ReturnsTheEntry()
    {
        await _catalog.IndexAsync(Entry("0001.md"));

        var entry = await _catalog.GetAsync("0001.md");

        entry.ShouldNotBeNull();
        entry.Keywords.ShouldBe(["identity", "0001.md"]);
        entry.IndexedAt.ShouldBe(Now);
        entry.ChunkCount.ShouldBe(3);
    }

    private static CatalogEntry Plan(string source, string type, string status, DateTimeOffset indexedAt, DateTimeOffset? effectiveAt = null) =>
        Entry(source) with
        {
            IndexedAt = indexedAt,
            EffectiveAt = effectiveAt,
            Fields = new Dictionary<string, string> { ["Type"] = type, ["Status"] = status },
        };

    [Test]
    public async Task IndexAsync_Fields_AreKept()
    {
        await _catalog.IndexAsync(Plan("plan-a.md", "plan", "Accepted", Now));

        (await _catalog.GetAsync("plan-a.md"))!.Fields.ShouldBe(new Dictionary<string, string> { ["Type"] = "plan", ["Status"] = "Accepted" }, ignoreOrder: true);
    }

    [Test]
    public async Task BrowseAsync_AFieldAndItsValue_ListsTheEntriesWithIt()
    {
        await _catalog.IndexAsync(Plan("plan-a.md", "plan", "Accepted", Now));
        await _catalog.IndexAsync(Plan("plan-b.md", "plan", "Closed", Now.AddDays(-1)));
        await _catalog.IndexAsync(Plan("adr-1.md", "adr", "Accepted", Now.AddDays(-2)));

        var plans = await _catalog.BrowseAsync(new CatalogBrowseOptions { Field = "type", Value = "PLAN" });

        plans.Select(e => e.Source).ShouldBe(["plan-a.md", "plan-b.md"]);
    }

    [Test]
    public async Task BrowseAsync_OrderedByEffectiveAt_NewestOwnDateFirstElseIndexedAt()
    {
        await _catalog.IndexAsync(Plan("old.md", "plan", "Closed", Now, effectiveAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await _catalog.IndexAsync(Plan("new.md", "plan", "Open", Now.AddDays(-2), effectiveAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        await _catalog.IndexAsync(Plan("undated.md", "plan", "Open", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));

        var listed = await _catalog.BrowseAsync(new CatalogBrowseOptions { Order = CatalogBrowseOrder.EffectiveAt });

        listed.Select(e => e.Source).ShouldBe(["new.md", "undated.md", "old.md"]);
    }

    [Test]
    public async Task IndexAsync_EffectiveAt_IsKept()
    {
        var written = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        await _catalog.IndexAsync(Entry("0001.md") with { EffectiveAt = written });
        await _catalog.IndexAsync(Entry("0002.md"));

        (await _catalog.GetAsync("0001.md"))!.EffectiveAt.ShouldBe(written);
        (await _catalog.GetAsync("0002.md"))!.EffectiveAt.ShouldBeNull();
    }

    [Test]
    public async Task IndexAsync_Replaces_IsKept()
    {
        await _catalog.IndexAsync(Entry("9110.md") with { Replaces = ["7230.md", "7231.md"] });

        (await _catalog.GetAsync("9110.md"))!.Replaces.ShouldBe(["7230.md", "7231.md"]);
    }

    [Test]
    public async Task IndexAsync_SameSource_Replaces()
    {
        await _catalog.IndexAsync(Entry("0001.md"));
        await _catalog.IndexAsync(Entry("0001.md", category: "plan"));

        (await _catalog.GetAsync("0001.md"))!.Category.ShouldBe("plan");
    }

    [Test]
    public async Task RemoveAsync_RemovesTheEntry()
    {
        await _catalog.IndexAsync(Entry("0001.md"));

        await _catalog.RemoveAsync("0001.md");

        (await _catalog.GetAsync("0001.md")).ShouldBeNull();
    }

    [Test]
    public async Task DiscoverAsync_LeavesOutSupersededEntries()
    {
        await _catalog.IndexAsync(Entry("0001.md"));
        await _catalog.IndexAsync(Entry("0002.md", supersededBy: "0003.md"));

        var results = await _catalog.DiscoverAsync("identity", topK: 5);

        results.Select(r => r.Entry.Source).ShouldBe(["0001.md"]);
    }

    [Test]
    public async Task BrowseAsync_FiltersByCategoryAndAge_NewestFirst()
    {
        await _catalog.IndexAsync(Entry("old.md", daysOld: 30));
        await _catalog.IndexAsync(Entry("new.md", daysOld: 1));
        await _catalog.IndexAsync(Entry("newer.md", daysOld: 0));
        await _catalog.IndexAsync(Entry("plan.md", category: "plan"));

        var results = await _catalog.BrowseAsync(new CatalogBrowseOptions { Category = "DESIGN", NotOlderThan = Now.AddDays(-7) });

        results.Select(e => e.Source).ShouldBe(["newer.md", "new.md"]);
    }
}
