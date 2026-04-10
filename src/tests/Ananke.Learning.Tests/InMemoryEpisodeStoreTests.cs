using Ananke.Learning;
using Ananke.Learning.Episodes;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class InMemoryEpisodeStoreTests
{
    private InMemoryEpisodeStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryEpisodeStore();
    }

    private static Episode MakeEpisode(
        string id,
        float terminalReward = 1.0f,
        int stepCount = 3,
        DateTimeOffset? completedAt = null) => new()
    {
        Id = id,
        Steps = Enumerable.Range(0, stepCount).Select(i => new EpisodeStep
        {
            StepIndex = i,
            EntryId = $"{id}_entry_{i}"
        }).ToList(),
        TerminalReward = terminalReward,
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow
    };

    // ── CommitAndRetrieveEpisode ─────────────────────────────────

    [Test]
    public async Task CommitAndRetrieveEpisode()
    {
        var episode = MakeEpisode("ep-1");

        var committed = await _store.CommitAsync(episode);
        committed.Id.ShouldBe("ep-1");

        var retrieved = await _store.GetAsync("ep-1");
        retrieved.ShouldNotBeNull();
        retrieved.Id.ShouldBe("ep-1");
        retrieved.Steps.Count.ShouldBe(3);
        retrieved.TerminalReward.ShouldBe(1.0f);
    }

    [Test]
    public async Task GetReturnsNullForMissingEpisode()
    {
        var result = await _store.GetAsync("nonexistent");
        result.ShouldBeNull();
    }

    // ── BrowseReturnsReverseChronological ────────────────────────

    [Test]
    public async Task BrowseReturnsReverseChronological()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.CommitAsync(MakeEpisode("ep-old", completedAt: now.AddMinutes(-30)));
        await _store.CommitAsync(MakeEpisode("ep-mid", completedAt: now.AddMinutes(-15)));
        await _store.CommitAsync(MakeEpisode("ep-new", completedAt: now));

        var results = await _store.BrowseAsync(0, 10);

        results.Count.ShouldBe(3);
        results[0].Id.ShouldBe("ep-new");
        results[1].Id.ShouldBe("ep-mid");
        results[2].Id.ShouldBe("ep-old");
    }

    [Test]
    public async Task BrowseRespectsOffsetAndLimit()
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            await _store.CommitAsync(MakeEpisode($"ep-{i}", completedAt: now.AddMinutes(i)));

        var page = await _store.BrowseAsync(1, 2);

        page.Count.ShouldBe(2);
        page[0].Id.ShouldBe("ep-3");
        page[1].Id.ShouldBe("ep-2");
    }

    // ── BrowseByOutcomeFilters ───────────────────────────────────

    [Test]
    public async Task BrowseByOutcomeFilters()
    {
        await _store.CommitAsync(MakeEpisode("win", terminalReward: 1.0f));
        await _store.CommitAsync(MakeEpisode("draw", terminalReward: 0.0f));
        await _store.CommitAsync(MakeEpisode("loss", terminalReward: -1.0f));
        await _store.CommitAsync(MakeEpisode("close-win", terminalReward: 0.5f));

        var wins = await _store.BrowseByOutcomeAsync(0.5f, 1.0f, 0, 10);

        wins.Count.ShouldBe(2);
        wins.ShouldAllBe(e => e.TerminalReward >= 0.5f);
    }

    [Test]
    public async Task BrowseByOutcomeReturnsEmptyWhenNoMatch()
    {
        await _store.CommitAsync(MakeEpisode("loss", terminalReward: -1.0f));

        var results = await _store.BrowseByOutcomeAsync(0.5f, 1.0f, 0, 10);

        results.ShouldBeEmpty();
    }

    // ── EntryWithEpisodeIdLinksToEpisode ─────────────────────────

    [Test]
    public async Task EntryWithEpisodeIdLinksToEpisode()
    {
        var embedder = new InMemoryEmbedder();
        var memory = new InMemoryEmpiricalMemory(embedder);

        var entry = await memory.CommitAsync(new EmpiricalEntry
        {
            Id = "entry-1",
            Kind = EmpiricalKind.Pattern,
            Tags = ["game_42", "move_0"],
            Source = "test",
            Description = SemanticDescription.FromText("opening move"),
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
            EpisodeId = "game_42",
            StepIndex = 0
        });

        var episode = await _store.CommitAsync(new Episode
        {
            Id = "game_42",
            Steps = [new EpisodeStep { StepIndex = 0, EntryId = entry.Id }],
            TerminalReward = 1.0f,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow
        });

        // Entry links to episode
        entry.EpisodeId.ShouldBe("game_42");
        entry.StepIndex.ShouldBe(0);

        // Episode links back to entry
        var retrieved = await _store.GetAsync("game_42");
        retrieved.ShouldNotBeNull();
        retrieved.Steps[0].EntryId.ShouldBe(entry.Id);
    }

    // ── StandaloneEntryHasNullEpisodeId ──────────────────────────

    [Test]
    public async Task StandaloneEntryHasNullEpisodeId()
    {
        var embedder = new InMemoryEmbedder();
        var memory = new InMemoryEmpiricalMemory(embedder);

        var entry = await memory.CommitAsync(new EmpiricalEntry
        {
            Id = "standalone-1",
            Kind = EmpiricalKind.Pattern,
            Tags = [],
            Source = "test",
            Description = SemanticDescription.FromText("standalone observation"),
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });

        entry.EpisodeId.ShouldBeNull();
        entry.StepIndex.ShouldBeNull();
    }

    // ── Metadata ─────────────────────────────────────────────────

    [Test]
    public async Task EpisodeMetadataIsPreserved()
    {
        var episode = new Episode
        {
            Id = "game-meta",
            Steps = [new EpisodeStep { StepIndex = 0, EntryId = "e1" }],
            TerminalReward = 1.0f,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                ["opponent"] = "human",
                ["moves"] = "7"
            }
        };

        await _store.CommitAsync(episode);
        var retrieved = await _store.GetAsync("game-meta");

        retrieved.ShouldNotBeNull();
        retrieved.Metadata["opponent"].ShouldBe("human");
        retrieved.Metadata["moves"].ShouldBe("7");
    }

    [Test]
    public async Task CommitReplacesExistingEpisode()
    {
        await _store.CommitAsync(MakeEpisode("ep-1", terminalReward: 0.5f));
        await _store.CommitAsync(MakeEpisode("ep-1", terminalReward: 1.0f));

        var retrieved = await _store.GetAsync("ep-1");
        retrieved.ShouldNotBeNull();
        retrieved.TerminalReward.ShouldBe(1.0f);
    }
}
