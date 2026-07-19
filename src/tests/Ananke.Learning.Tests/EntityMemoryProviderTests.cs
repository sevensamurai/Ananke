using Ananke.Abstractions.Agents;
using Ananke.Learning.EntityMemory;
using Ananke.Learning.Episodes;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Memory;
using Shouldly;


using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Tests;

[TestFixture]
public class EntityMemoryProviderTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryConversationMemory _conversations = null!;
    private InMemoryEmpiricalMemory _empirical = null!;
    private InMemoryKnowledgeStore _knowledge = null!;
    private InMemoryEpisodeStore _episodes = null!;
    private EntityMemoryProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _conversations = new InMemoryConversationMemory();
        _empirical = new InMemoryEmpiricalMemory(_embedder);
        _knowledge = new InMemoryKnowledgeStore(_embedder);
        _episodes = new InMemoryEpisodeStore();
        _provider = new EntityMemoryProvider(_conversations, _empirical, _knowledge, _episodes);
    }

    // ── Provider lifecycle ───────────────────────────────────────

    [Test]
    public void GetOrCreate_ReturnsSameInstance_ForSameEntityId()
    {
        var a = _provider.GetOrCreate("customer-1");
        var b = _provider.GetOrCreate("customer-1");

        a.ShouldBeSameAs(b);
        a.EntityId.ShouldBe("customer-1");
    }

    [Test]
    public void GetOrCreate_ReturnsDifferentInstances_ForDifferentEntityIds()
    {
        var a = _provider.GetOrCreate("customer-1");
        var b = _provider.GetOrCreate("customer-2");

        a.ShouldNotBeSameAs(b);
        a.EntityId.ShouldBe("customer-1");
        b.EntityId.ShouldBe("customer-2");
    }

    [Test]
    public async Task EvictAsync_RemovesCachedFacade_SubsequentGetCreatesNew()
    {
        var first = _provider.GetOrCreate("customer-1");
        await _provider.EvictAsync("customer-1");
        var second = _provider.GetOrCreate("customer-1");

        second.ShouldNotBeSameAs(first);
        second.EntityId.ShouldBe("customer-1");
    }

    // ── Conversation scoping ────────────────────────────────────

    [Test]
    public async Task Conversations_AreIsolatedByEntity()
    {
        var mem1 = _provider.GetOrCreate("user-A");
        var mem2 = _provider.GetOrCreate("user-B");

        await mem1.Conversations.AddAsync("session-1", AgentMessage.User("Hello from A"));
        await mem2.Conversations.AddAsync("session-1", AgentMessage.User("Hello from B"));

        var histA = await mem1.Conversations.GetHistoryAsync("session-1");
        var histB = await mem2.Conversations.GetHistoryAsync("session-1");

        histA.Count.ShouldBe(1);
        histA[0].Content.ShouldBe("Hello from A");
        histB.Count.ShouldBe(1);
        histB[0].Content.ShouldBe("Hello from B");
    }

    // ── Empirical memory scoping ────────────────────────────────

    [Test]
    public async Task Empirical_Commit_InjectsEntityId()
    {
        var mem = _provider.GetOrCreate("customer-42");

        var entry = MakePattern("p1", "prefers minimalist design");
        var committed = await mem.Empirical.CommitAsync(entry);

        committed.EntityId.ShouldBe("customer-42");
    }

    [Test]
    public async Task Empirical_Recall_OnlyReturnsEntityEntries()
    {
        var mem1 = _provider.GetOrCreate("customer-A");
        var mem2 = _provider.GetOrCreate("customer-B");

        await mem1.Empirical.CommitAsync(MakePattern("p1", "likes minimalist furniture"));
        await mem2.Empirical.CommitAsync(MakePattern("p2", "likes baroque furniture"));

        var results = await mem1.Empirical.RecallAsync("furniture preferences");

        results.Count.ShouldBe(1);
        results[0].Entry.EntityId.ShouldBe("customer-A");
    }

    [Test]
    public async Task Empirical_Browse_DefaultsScopeToEntity()
    {
        var mem1 = _provider.GetOrCreate("customer-A");
        var mem2 = _provider.GetOrCreate("customer-B");

        await mem1.Empirical.CommitAsync(MakePattern("p1", "pattern A"));
        await mem2.Empirical.CommitAsync(MakePattern("p2", "pattern B"));

        var browsed = await mem1.Empirical.BrowseAsync(0, 100);

        browsed.Count.ShouldBe(1);
        browsed[0].EntityId.ShouldBe("customer-A");
    }

    [Test]
    public async Task Empirical_Dedup_DoesNotMergeAcrossEntities()
    {
        var mem1 = _provider.GetOrCreate("customer-A");
        var mem2 = _provider.GetOrCreate("customer-B");

        // Same description committed for two different entities
        await mem1.Empirical.CommitAsync(MakePattern("p1", "prefers minimalist design"));
        await mem2.Empirical.CommitAsync(MakePattern("p2", "prefers minimalist design"));

        // Both should exist as separate entries — no cross-entity merge
        _empirical.Count.ShouldBe(2);
    }

    // ── Knowledge store scoping ─────────────────────────────────

    [Test]
    public async Task Knowledge_Upsert_InjectsEntityMetadata()
    {
        var mem = _provider.GetOrCreate("customer-42");

        await mem.Knowledge.UpsertAsync([new KnowledgeDocument
        {
            Id = "doc-1",
            Text = "Minimalist design principles"
        }]);

        var results = await _knowledge.SearchAsync("minimalist", new SearchOptions
        {
            Filter = new KnowledgeFilter { ["entity_id"] = "customer-42" }
        });

        results.Count.ShouldBe(1);
        results[0].Metadata["entity_id"].ShouldBe("customer-42");
    }

    [Test]
    public async Task Knowledge_Search_OnlyReturnsEntityDocuments()
    {
        var mem1 = _provider.GetOrCreate("customer-A");
        var mem2 = _provider.GetOrCreate("customer-B");

        await mem1.Knowledge.UpsertAsync([new KnowledgeDocument
        {
            Id = "doc-A",
            Text = "Customer A preferences"
        }]);

        await mem2.Knowledge.UpsertAsync([new KnowledgeDocument
        {
            Id = "doc-B",
            Text = "Customer B preferences"
        }]);

        var results = await mem1.Knowledge.SearchAsync("preferences");

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("doc-A");
    }

    [Test]
    public async Task Knowledge_Delete_OnlyDeletesEntityDocuments()
    {
        var mem1 = _provider.GetOrCreate("customer-A");
        var mem2 = _provider.GetOrCreate("customer-B");

        await mem1.Knowledge.UpsertAsync([new KnowledgeDocument { Id = "doc-A", Text = "A data" }]);
        await mem2.Knowledge.UpsertAsync([new KnowledgeDocument { Id = "doc-B", Text = "B data" }]);

        await mem1.Knowledge.DeleteAsync(new KnowledgeFilter());

        // Only customer-A's document should be deleted
        _knowledge.Count.ShouldBe(1);
        var remaining = await _knowledge.SearchAsync("data");
        remaining[0].Metadata["entity_id"].ShouldBe("customer-B");
    }

    // ── Episode store scoping ───────────────────────────────────

    [Test]
    public async Task Episodes_Commit_InjectsEntityId()
    {
        var mem = _provider.GetOrCreate("customer-42");

        var episode = MakeEpisode("ep-1", 1.0f);
        var committed = await mem.Episodes.CommitAsync(episode);

        committed.EntityId.ShouldBe("customer-42");
    }

    [Test]
    public async Task Episodes_Browse_DefaultsScopeToEntity()
    {
        var mem1 = _provider.GetOrCreate("customer-A");
        var mem2 = _provider.GetOrCreate("customer-B");

        await mem1.Episodes.CommitAsync(MakeEpisode("ep-1", 1.0f));
        await mem2.Episodes.CommitAsync(MakeEpisode("ep-2", 0.5f));

        var browsed = await mem1.Episodes.BrowseAsync(0, 100);

        browsed.Count.ShouldBe(1);
        browsed[0].EntityId.ShouldBe("customer-A");
    }

    // ── Eviction preserves persisted data ───────────────────────

    [Test]
    public async Task EvictAsync_DoesNotDeletePersistedData()
    {
        var mem = _provider.GetOrCreate("customer-42");
        await mem.Empirical.CommitAsync(MakePattern("p1", "a pattern"));

        await _provider.EvictAsync("customer-42");

        // Data still exists in the shared store
        _empirical.Count.ShouldBe(1);

        // New facade still sees the same data
        var mem2 = _provider.GetOrCreate("customer-42");
        var results = await mem2.Empirical.RecallAsync("a pattern");
        results.Count.ShouldBe(1);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static EmpiricalEntry MakePattern(
        string id, string description, float confidence = 0.5f) => new()
        {
            Id = id,
            Kind = EmpiricalKind.Pattern,
            Tags = [],
            Source = "test",
            Description = SemanticDescription.FromText(description),
            Confidence = confidence,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        };

    private static Episode MakeEpisode(string id, float reward) => new()
    {
        Id = id,
        Steps = [new EpisodeStep { StepIndex = 0, EntryId = "e1" }],
        TerminalReward = reward,
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        CompletedAt = DateTimeOffset.UtcNow
    };
}
