using Ananke.Learning;
using Ananke.Learning.Features;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class TagImportanceTrackerTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryEmpiricalMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _memory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 1.0f);
    }

    private async Task CommitEntryAsync(
        string id,
        IReadOnlyList<string> tags,
        float valence)
    {
        await _memory.CommitAsync(new EmpiricalEntry
        {
            Id = id,
            Kind = EmpiricalKind.Pattern,
            Tags = tags,
            Source = "test",
            Description = SemanticDescription.FromText($"entry {id} {Guid.NewGuid():N}"),
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
            Valence = valence
        });
    }

    // ── PositiveTagsGetHighImportance ────────────────────────────

    [Test]
    public async Task PositiveTagsGetHighImportance()
    {
        // 10 entries all with positive valence and the same tag
        for (var i = 0; i < 10; i++)
            await CommitEntryAsync($"pos-{i}", ["winning-move"], valence: 0.8f);

        var tracker = new TagImportanceTracker(
            new TagImportanceOptions { MinSampleSize = 5 });

        var map = await tracker.ComputeAsync(_memory);

        map.ShouldNotBeNull();
        map.GetImportance("winning-move").ShouldBe(1.0f);
        map.EntriesAnalyzed.ShouldBe(10);
    }

    // ── MixedTagsGetNeutralImportance ────────────────────────────

    [Test]
    public async Task MixedTagsGetNeutralImportance()
    {
        // 5 positive and 5 negative entries with the same tag
        for (var i = 0; i < 5; i++)
            await CommitEntryAsync($"mix-pos-{i}", ["center-play"], valence: 0.5f);
        for (var i = 0; i < 5; i++)
            await CommitEntryAsync($"mix-neg-{i}", ["center-play"], valence: -0.5f);

        var tracker = new TagImportanceTracker(
            new TagImportanceOptions { MinSampleSize = 5 });

        var map = await tracker.ComputeAsync(_memory);

        map.ShouldNotBeNull();
        map.GetImportance("center-play").ShouldBe(0.5f, tolerance: 0.01f);
    }

    // ── NegativeTagsGetLowImportance ─────────────────────────────

    [Test]
    public async Task NegativeTagsGetLowImportance()
    {
        // 10 entries all with negative valence
        for (var i = 0; i < 10; i++)
            await CommitEntryAsync($"neg-{i}", ["losing-trap"], valence: -0.7f);

        var tracker = new TagImportanceTracker(
            new TagImportanceOptions { MinSampleSize = 5 });

        var map = await tracker.ComputeAsync(_memory);

        map.ShouldNotBeNull();
        map.GetImportance("losing-trap").ShouldBe(0.0f);
    }

    // ── UnseenTagsReturnNeutral ──────────────────────────────────

    [Test]
    public async Task UnseenTagsReturnNeutral()
    {
        // Populate enough entries to pass the sample guard
        for (var i = 0; i < 10; i++)
            await CommitEntryAsync($"other-{i}", ["known-tag"], valence: 0.5f);

        var tracker = new TagImportanceTracker(
            new TagImportanceOptions { MinSampleSize = 5 });

        var map = await tracker.ComputeAsync(_memory);

        map.ShouldNotBeNull();
        // "never-seen" is not in the map — should default to 1.0 (neutral)
        map.GetImportance("never-seen").ShouldBe(1.0f);
    }

    // ── MinSampleSizeGuard ───────────────────────────────────────

    [Test]
    public async Task MinSampleSizeGuard()
    {
        // Only 3 entries with valence — below default min sample of 10
        for (var i = 0; i < 3; i++)
            await CommitEntryAsync($"few-{i}", ["sparse-tag"], valence: 0.5f);

        var tracker = new TagImportanceTracker();

        var map = await tracker.ComputeAsync(_memory);

        // Should return null — not enough data
        map.ShouldBeNull();
    }
}
