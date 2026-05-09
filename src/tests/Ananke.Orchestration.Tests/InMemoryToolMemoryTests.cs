using Ananke.Abstractions.Tools;
using Ananke.Orchestration.Tools.Gating;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class InMemoryToolMemoryTests
{
    private InMemoryToolMemory _memory = null!;

    [SetUp]
    public void SetUp() => _memory = new InMemoryToolMemory();

    // ── Upsert / Remove ──────────────────────────────────────────────

    [Test]
    public async Task UpsertAsync_ThenRecall_ReturnsEntry()
    {
        var entry = MakeEntry("search", "kit", "Searches the web for information");
        await _memory.UpsertAsync(entry);

        var results = await _memory.RecallAsync("search web");

        results.Count.ShouldBe(1);
        results[0].ToolName.ShouldBe("search");
    }

    [Test]
    public async Task UpsertAsync_Overwrites_ExistingEntry()
    {
        await _memory.UpsertAsync(MakeEntry("ping", "kit", "Ping a host"));
        await _memory.UpsertAsync(MakeEntry("ping", "kit", "Ping an updated host") with { HitCount = 5 });

        var results = await _memory.RecallAsync("ping");

        results.Count.ShouldBe(1);
        results[0].Description.ShouldBe("Ping an updated host");
        results[0].HitCount.ShouldBe(5);
    }

    [Test]
    public async Task RemoveAsync_RemovesEntry_FromRecall()
    {
        await _memory.UpsertAsync(MakeEntry("delete", "kit", "Deletes a record"));
        await _memory.RemoveAsync("kit", "delete");

        var results = await _memory.RecallAsync("delete");

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task RemoveAsync_NonExistent_IsNoOp()
    {
        await Should.NotThrowAsync(() => _memory.RemoveAsync("no-kit", "no-tool"));
    }

    // ── RecallAsync ──────────────────────────────────────────────────

    [Test]
    public async Task RecallAsync_RanksHigherScoreFirst()
    {
        await _memory.UpsertAsync(MakeEntry("buy", "kit", "Buys shares on the stock market"));
        await _memory.UpsertAsync(MakeEntry("ping", "kit", "Pings a server host"));

        // "stock market buy shares" should match "buy" much better than "ping"
        var results = await _memory.RecallAsync("stock market buy shares");

        results[0].ToolName.ShouldBe("buy");
    }

    [Test]
    public async Task RecallAsync_TagsWeightedHigher_ThanDescription()
    {
        await _memory.UpsertAsync(MakeEntry("trade", "kit", "Execute a trade", tags: ["stock", "finance"]));
        await _memory.UpsertAsync(MakeEntry("news", "kit", "Get stock news headlines"));

        // query contains the exact tag "stock"
        var results = await _memory.RecallAsync("stock");

        results[0].ToolName.ShouldBe("trade");
    }

    [Test]
    public async Task RecallAsync_RespectsTopK()
    {
        for (var i = 0; i < 10; i++)
            await _memory.UpsertAsync(MakeEntry($"tool{i}", "kit", $"Tool number {i}"));

        var results = await _memory.RecallAsync("tool", topK: 3);

        results.Count.ShouldBeLessThanOrEqualTo(3);
    }

    [Test]
    public async Task RecallAsync_ExcludesOfflineTools()
    {
        await _memory.UpsertAsync(MakeEntry("broken", "kit", "A broken tool") with { Health = ToolHealth.Offline });
        await _memory.UpsertAsync(MakeEntry("ok", "kit", "A working tool"));

        var results = await _memory.RecallAsync("tool broken ok");

        results.ShouldNotContain(e => e.ToolName == "broken");
    }

    [Test]
    public async Task RecallAsync_TagFilter_NarrowsCandidates()
    {
        await _memory.UpsertAsync(MakeEntry("buy", "kit", "Buy a thing", tags: ["trading"]));
        await _memory.UpsertAsync(MakeEntry("read", "kit", "Read a thing", tags: ["docs"]));

        var results = await _memory.RecallAsync("thing", tagFilter: ["trading"]);

        results.Count.ShouldBe(1);
        results[0].ToolName.ShouldBe("buy");
    }

    // ── MarkHealthAsync ──────────────────────────────────────────────

    [Test]
    public async Task MarkHealthAsync_ChangesHealthState()
    {
        await _memory.UpsertAsync(MakeEntry("flaky", "kit", "An occasionally failing tool"));
        await _memory.MarkHealthAsync("kit", "flaky", ToolHealth.Cooldown);

        // Cooldown tools are not excluded (only Offline is)
        var results = await _memory.RecallAsync("failing tool");
        results.Count.ShouldBe(1);
        results[0].Health.ShouldBe(ToolHealth.Cooldown);
    }

    [Test]
    public async Task MarkHealthAsync_NonExistent_IsNoOp()
    {
        await Should.NotThrowAsync(() => _memory.MarkHealthAsync("no-kit", "no-tool", ToolHealth.Offline));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ToolMemoryEntry MakeEntry(
        string toolName, string kitName, string description,
        IReadOnlyList<string>? tags = null) =>
        new()
        {
            ToolName = toolName,
            KitName = kitName,
            Description = description,
            Tags = tags ?? []
        };
}
