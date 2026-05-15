using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Organics.Division;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class DomainAffinityMemoryTests
{
    private FakeEmpiricalMemory _inner = null!;
    private DomainAffinityMemory _affinity = null!;

    [SetUp]
    public void SetUp()
    {
        _inner = new FakeEmpiricalMemory();
        _affinity = new DomainAffinityMemory(_inner, ["search", "catalog"]);
    }

    private static EmpiricalEntry MakeEntry(
        string id = "e1",
        IReadOnlyList<string>? tags = null) => new()
    {
        Id = id,
        Kind = EmpiricalKind.Pattern,
        Tags = tags ?? [],
        Source = "test",
        Description = SemanticDescription.FromText("test pattern"),
        Confidence = 0.8f,
        ObservationCount = 1,
        Evidence = [],
        FirstObserved = DateTimeOffset.UtcNow,
        LastObserved = DateTimeOffset.UtcNow
    };

    // ── CommitAsync ─────────────────────────────────────────────────

    [Test]
    public async Task CommitAsync_InjectsDomainTags()
    {
        var entry = MakeEntry(tags: []);

        await _affinity.CommitAsync(entry);

        _inner.LastCommitted.ShouldNotBeNull();
        _inner.LastCommitted.Tags.ShouldContain("search");
        _inner.LastCommitted.Tags.ShouldContain("catalog");
    }

    [Test]
    public async Task CommitAsync_PreservesExistingTags()
    {
        var entry = MakeEntry(tags: ["existing-tag"]);

        await _affinity.CommitAsync(entry);

        _inner.LastCommitted.ShouldNotBeNull();
        _inner.LastCommitted.Tags.ShouldContain("existing-tag");
        _inner.LastCommitted.Tags.ShouldContain("search");
        _inner.LastCommitted.Tags.ShouldContain("catalog");
    }

    [Test]
    public async Task CommitAsync_NoDuplicateTags()
    {
        var entry = MakeEntry(tags: ["search", "other"]);

        await _affinity.CommitAsync(entry);

        _inner.LastCommitted.ShouldNotBeNull();
        _inner.LastCommitted.Tags.Count(t => t == "search").ShouldBe(1);
    }

    // ── RecallAsync ─────────────────────────────────────────────────

    [Test]
    public async Task RecallAsync_EnrichesSituation()
    {
        await _affinity.RecallAsync("find books about dragons");

        _inner.LastRecallSituation.ShouldNotBeNull();
        _inner.LastRecallSituation!.ShouldContain("find books about dragons");
        _inner.LastRecallSituation.ShouldContain("search");
        _inner.LastRecallSituation.ShouldContain("catalog");
    }

    // ── Pass-through methods ────────────────────────────────────────

    [Test]
    public async Task ReinforceAsync_PassesThrough()
    {
        await _affinity.ReinforceAsync("e1", new Reinforcement
        {
            Reward = 1.0f,
            NewEvidence = ["test-evidence"],
            Source = "test"
        });

        _inner.LastReinforceId.ShouldBe("e1");
    }

    [Test]
    public async Task ContradictAsync_PassesThrough()
    {
        await _affinity.ContradictAsync("e1", "wrong");

        _inner.LastContradictId.ShouldBe("e1");
    }

    [Test]
    public async Task GetAsync_PassesThrough()
    {
        await _affinity.GetAsync("e1");

        _inner.LastGetId.ShouldBe("e1");
    }

    [Test]
    public async Task BrowseAsync_PassesThrough()
    {
        await _affinity.BrowseAsync(0, 10);

        _inner.BrowseCalled.ShouldBeTrue();
    }

    [Test]
    public async Task BrowseAsync_WithOptions_PassesThrough()
    {
        await _affinity.BrowseAsync(new BrowseOptions());

        _inner.BrowseOptionsCalled.ShouldBeTrue();
    }

    [Test]
    public async Task CountAsync_PassesThrough()
    {
        await _affinity.CountAsync();

        _inner.CountCalled.ShouldBeTrue();
    }

    [Test]
    public async Task MarkConsolidatedAsync_PassesThrough()
    {
        await _affinity.MarkConsolidatedAsync("e1", "doc-1");

        _inner.LastConsolidatedId.ShouldBe("e1");
    }

    // ── Fake ────────────────────────────────────────────────────────

    private sealed class FakeEmpiricalMemory : IEmpiricalMemory
    {
        public EmpiricalEntry? LastCommitted { get; private set; }
        public string? LastRecallSituation { get; private set; }
        public string? LastReinforceId { get; private set; }
        public string? LastContradictId { get; private set; }
        public string? LastGetId { get; private set; }
        public string? LastConsolidatedId { get; private set; }
        public bool BrowseCalled { get; private set; }
        public bool BrowseOptionsCalled { get; private set; }
        public bool CountCalled { get; private set; }

        public Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default)
        {
            LastCommitted = entry;
            return Task.FromResult(entry);
        }

        public Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
            string situation, RecallOptions? options = null, CancellationToken ct = default)
        {
            LastRecallSituation = situation;
            return Task.FromResult<IReadOnlyList<EmpiricalMatch>>([]);
        }

        public Task ReinforceAsync(string entryId, Reinforcement reinforcement, CancellationToken ct = default)
        {
            LastReinforceId = entryId;
            return Task.CompletedTask;
        }

        public Task ContradictAsync(string entryId, string reason, CancellationToken ct = default)
        {
            LastContradictId = entryId;
            return Task.CompletedTask;
        }

        public Task<EmpiricalEntry?> GetAsync(string entryId, CancellationToken ct = default)
        {
            LastGetId = entryId;
            return Task.FromResult<EmpiricalEntry?>(null);
        }

        public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
            int offset, int limit, EmpiricalKind? kind = null,
            string? entityId = null, CancellationToken ct = default)
        {
            BrowseCalled = true;
            return Task.FromResult<IReadOnlyList<EmpiricalEntry>>([]);
        }

        public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
            BrowseOptions options, CancellationToken ct = default)
        {
            BrowseOptionsCalled = true;
            return Task.FromResult<IReadOnlyList<EmpiricalEntry>>([]);
        }

        public Task<int> CountAsync(BrowseOptions? options = null, CancellationToken ct = default)
        {
            CountCalled = true;
            return Task.FromResult(0);
        }

        public Task MarkConsolidatedAsync(string entryId, string knowledgeDocId, CancellationToken ct = default)
        {
            LastConsolidatedId = entryId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmpiricalMatch>> PairRecallAsync(
            EmpiricalEntry reference, PairRecallOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmpiricalMatch>>([]);
    }
}
