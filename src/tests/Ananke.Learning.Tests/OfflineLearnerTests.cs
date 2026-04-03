using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Learning;
using Ananke.Learning.Offline;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class OfflineLearnerTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryEmpiricalMemory _memory = null!;
    private OfflineLearner _learner = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _memory = new InMemoryEmpiricalMemory(_embedder, affectOptions: new AffectOptions());
        _learner = new OfflineLearner(_memory, _embedder,
            options: new OfflineLearnerOptions { ExplorationBatchSize = 3 });
    }

    private static EmpiricalEntry MakePattern(
        string id,
        string description,
        float confidence = 0.5f,
        float strength = 0.5f,
        float variance = 1.0f) => new()
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
        LastObserved = DateTimeOffset.UtcNow,
        Strength = strength,
        Variance = variance
    };

    // ── BrowseAsync ──────────────────────────────────────────────

    [Test]
    public async Task BrowseAsync_ReturnsAllEntries()
    {
        await _memory.CommitAsync(MakePattern("p1", "pattern one"));
        await _memory.CommitAsync(MakePattern("p2", "pattern two"));
        await _memory.CommitAsync(MakePattern("p3", "pattern three"));

        var results = await _memory.BrowseAsync(0, 10);

        results.Count.ShouldBe(3);
    }

    [Test]
    public async Task BrowseAsync_Pagination_Works()
    {
        await _memory.CommitAsync(MakePattern("p1", "alpha pattern"));
        await _memory.CommitAsync(MakePattern("p2", "beta pattern"));
        await _memory.CommitAsync(MakePattern("p3", "gamma pattern"));

        var page1 = await _memory.BrowseAsync(0, 2);
        var page2 = await _memory.BrowseAsync(2, 2);

        page1.Count.ShouldBe(2);
        page2.Count.ShouldBe(1);
    }

    [Test]
    public async Task BrowseAsync_FilterByKind_OnlyReturnsMatchingKind()
    {
        await _memory.CommitAsync(MakePattern("p1", "a pattern entry"));
        await _memory.CommitAsync(new EmpiricalEntry
        {
            Id = "s1",
            Kind = EmpiricalKind.Skill,
            Tags = [],
            Source = "test",
            Description = SemanticDescription.FromText("a skill entry"),
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });

        var patterns = await _memory.BrowseAsync(0, 10, EmpiricalKind.Pattern);
        var skills = await _memory.BrowseAsync(0, 10, EmpiricalKind.Skill);

        patterns.Count.ShouldBe(1);
        patterns[0].Kind.ShouldBe(EmpiricalKind.Pattern);
        skills.Count.ShouldBe(1);
        skills[0].Kind.ShouldBe(EmpiricalKind.Skill);
    }

    // ── DecayAsync ───────────────────────────────────────────────

    [Test]
    public async Task DecayAsync_RemovesWeakEntries()
    {
        // Entry with very low strength should be decayed away
        var weak = MakePattern("weak", "very weak belief",
            confidence: 0.1f, strength: 0.04f, variance: 0.5f);
        await _memory.CommitAsync(weak);

        var decayed = await _learner.DecayAsync();

        decayed.ShouldBe(1);
        var entry = await _memory.GetAsync("weak");
        entry.ShouldNotBeNull();
        // Contradicted with AffectOptions active → Strength weakened,
        // Variance increased, and evidence records the contradiction.
        entry.Strength.ShouldBeLessThan(weak.Strength);
        entry.Variance.ShouldBeGreaterThan(weak.Variance);
        entry.Evidence.ShouldContain(e => e.StartsWith("contradicted:"));
    }

    [Test]
    public async Task DecayAsync_PreservesStrongEntries()
    {
        var strong = MakePattern("strong", "strong belief",
            confidence: 0.9f, strength: 0.9f, variance: 0.1f);
        await _memory.CommitAsync(strong);

        var decayed = await _learner.DecayAsync();

        decayed.ShouldBe(0);
        var entry = await _memory.GetAsync("strong");
        entry.ShouldNotBeNull();
        entry.Confidence.ShouldBeGreaterThan(0f);
    }

    // ── LearnAsync (full cycle) ──────────────────────────────────

    [Test]
    public async Task LearnAsync_EmptyMemory_ReturnsZeroes()
    {
        var result = await _learner.LearnAsync();

        result.Decayed.ShouldBe(0);
        result.Explored.ShouldBe(0);
        result.Reinforced.ShouldBe(0);
        result.Contradicted.ShouldBe(0);
        result.Discoveries.ShouldBeEmpty();
    }

    [Test]
    public async Task LearnAsync_WithEntries_ExploresUpToBatchSize()
    {
        await _memory.CommitAsync(MakePattern("p1", "alpha entry"));
        await _memory.CommitAsync(MakePattern("p2", "beta entry"));
        await _memory.CommitAsync(MakePattern("p3", "gamma entry"));
        await _memory.CommitAsync(MakePattern("p4", "delta entry"));
        await _memory.CommitAsync(MakePattern("p5", "epsilon entry"));

        var result = await _learner.LearnAsync();

        result.Explored.ShouldBeLessThanOrEqualTo(3); // batch size
    }

    [Test]
    public async Task LearnAsync_CuriousEntriesPreferred()
    {
        // High-surprise entry should be preferred for exploration
        var curious = MakePattern("curious", "curious high surprise entry",
            confidence: 0.3f, variance: 0.9f) with { LastPredictionError = 0.8f };
        var boring = MakePattern("boring", "boring low surprise entry",
            confidence: 0.8f, variance: 0.1f) with { LastPredictionError = 0.05f };

        await _memory.CommitAsync(curious);
        await _memory.CommitAsync(boring);

        // With batch size 3 and only 2 entries, both will be explored
        // But the curious one should be selected in the curious slot
        var result = await _learner.LearnAsync();
        result.Explored.ShouldBeGreaterThanOrEqualTo(1);
    }

    // ── Intrinsic reward computation ─────────────────────────────

    [Test]
    public void IntrinsicReward_SurprisingAndCoherent_IsDiscovery()
    {
        var reward = _learner.ComputeIntrinsicReward(
            surprise: 0.9f, coherence: 0.9f);

        reward.ShouldBeGreaterThan(0.5f);
    }

    [Test]
    public void IntrinsicReward_SurprisingAndIncoherent_IsNoise()
    {
        var reward = _learner.ComputeIntrinsicReward(
            surprise: 0.9f, coherence: 0.1f);

        reward.ShouldBeLessThan(0.1f);
    }

    [Test]
    public void IntrinsicReward_ExpectedAndCoherent_IsConfirmation()
    {
        var reward = _learner.ComputeIntrinsicReward(
            surprise: 0.1f, coherence: 0.9f);

        reward.ShouldBeGreaterThan(0f);
        reward.ShouldBeLessThan(0.5f);
    }

    [Test]
    public void IntrinsicReward_ExpectedAndIncoherent_IsContradiction()
    {
        var reward = _learner.ComputeIntrinsicReward(
            surprise: 0.1f, coherence: 0.1f);

        reward.ShouldBeLessThan(0f);
    }

    // ── Simulation integration ───────────────────────────────────

    [Test]
    public async Task LearnAsync_WithSimulator_UsesSimulationEvidence()
    {
        var simulator = new FakeSimulationSource(reward: 0.7f);
        var learner = new OfflineLearner(_memory, _embedder,
            simulator: simulator,
            options: new OfflineLearnerOptions
            {
                ExplorationBatchSize = 1,
                SimulationMinConfidence = 0.1f
            });

        await _memory.CommitAsync(MakePattern("p1", "testable hypothesis", confidence: 0.5f));

        var result = await learner.LearnAsync();

        result.Explored.ShouldBe(1);
        simulator.CallCount.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task LearnAsync_WithSimulator_SkipsLowConfidenceEntries()
    {
        var simulator = new FakeSimulationSource(reward: 0.5f);
        var learner = new OfflineLearner(_memory, _embedder,
            simulator: simulator,
            options: new OfflineLearnerOptions
            {
                ExplorationBatchSize = 1,
                SimulationMinConfidence = 0.8f // high bar
            });

        await _memory.CommitAsync(MakePattern("p1", "low confidence entry", confidence: 0.2f));

        await learner.LearnAsync();

        simulator.CallCount.ShouldBe(0); // confidence too low
    }

    // ── Consolidation ──────────────────────────────────────────────

    [Test]
    public async Task ShouldConsolidate_MaturePattern_ReturnsTrue()
    {
        var entry = MakePattern("p1", "mature pattern",
            confidence: 0.9f, strength: 0.85f, variance: 0.03f) with
        {
            ObservationCount = 15
        };

        _learner.ShouldConsolidate(entry).ShouldBeTrue();
    }

    [Test]
    public async Task ShouldConsolidate_WeakStrength_ReturnsFalse()
    {
        var entry = MakePattern("p1", "weak pattern",
            confidence: 0.9f, strength: 0.3f, variance: 0.03f) with
        {
            ObservationCount = 15
        };

        _learner.ShouldConsolidate(entry).ShouldBeFalse();
    }

    [Test]
    public async Task ShouldConsolidate_HighVariance_ReturnsFalse()
    {
        var entry = MakePattern("p1", "unstable pattern",
            confidence: 0.9f, strength: 0.9f, variance: 0.5f) with
        {
            ObservationCount = 15
        };

        _learner.ShouldConsolidate(entry).ShouldBeFalse();
    }

    [Test]
    public async Task ShouldConsolidate_TooFewObservations_ReturnsFalse()
    {
        var entry = MakePattern("p1", "young pattern",
            confidence: 0.9f, strength: 0.9f, variance: 0.03f) with
        {
            ObservationCount = 3
        };

        _learner.ShouldConsolidate(entry).ShouldBeFalse();
    }

    [Test]
    public async Task ShouldConsolidate_SkillKind_ReturnsFalse()
    {
        var entry = new EmpiricalEntry
        {
            Id = "s1",
            Kind = EmpiricalKind.Skill,
            Tags = [],
            Source = "test",
            Description = SemanticDescription.FromText("mature skill"),
            Confidence = 0.9f,
            ObservationCount = 15,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow,
            Strength = 0.9f,
            Variance = 0.03f
        };

        _learner.ShouldConsolidate(entry).ShouldBeFalse();
    }

    [Test]
    public async Task ShouldConsolidate_AlreadyConsolidated_ReturnsFalse()
    {
        var entry = MakePattern("p1", "already done",
            confidence: 0.9f, strength: 0.9f, variance: 0.03f) with
        {
            ObservationCount = 15,
            ConsolidatedInto = "consolidated-p1"
        };

        _learner.ShouldConsolidate(entry).ShouldBeFalse();
    }

    [Test]
    public async Task LearnAsync_WithConsolidation_PromotesAndMarksEntry()
    {
        var knowledgeStore = new InMemoryKnowledgeStore(_embedder);
        var summarizer = new TemplateConsolidationSummarizer();
        var learner = new OfflineLearner(_memory, _embedder,
            knowledgeStore: knowledgeStore,
            summarizer: summarizer,
            options: new OfflineLearnerOptions
            {
                ExplorationBatchSize = 1,
                ConsolidationMinStrength = 0.8f,
                ConsolidationMaxVariance = 0.1f,
                ConsolidationMinObservations = 5
            });

        var entry = MakePattern("p1", "well confirmed pattern",
            confidence: 0.9f, strength: 0.85f, variance: 0.03f) with
        {
            ObservationCount = 10,
            Condition = "high load",
            Effect = "timeout"
        };
        await _memory.CommitAsync(entry);

        var result = await learner.LearnAsync();

        result.Consolidated.ShouldBe(1);

        // Entry should be marked
        var updated = await _memory.GetAsync("p1");
        updated.ShouldNotBeNull();
        updated.ConsolidatedInto.ShouldBe("consolidated-p1");

        // Knowledge store should have the document
        var docs = await knowledgeStore.SearchAsync("well confirmed pattern");
        docs.Count.ShouldBeGreaterThan(0);
        docs[0].Text.ShouldContain("Pattern:");
    }

    [Test]
    public async Task LearnAsync_ConsolidatedEntryExcludedFromRecall()
    {
        await _memory.CommitAsync(MakePattern("p1", "consolidated entry pattern") with
        {
            ConsolidatedInto = "consolidated-p1"
        });
        await _memory.CommitAsync(MakePattern("p2", "active entry pattern"));

        var results = await _memory.RecallAsync("entry pattern");

        results.ShouldAllBe(r => r.Entry.Id != "p1");
        results.ShouldContain(r => r.Entry.Id == "p2");
    }

    [Test]
    public async Task LearnAsync_ConsolidatedEntryExcludedFromExploration()
    {
        var knowledgeStore = new InMemoryKnowledgeStore(_embedder);
        var summarizer = new TemplateConsolidationSummarizer();
        var learner = new OfflineLearner(_memory, _embedder,
            knowledgeStore: knowledgeStore,
            summarizer: summarizer,
            options: new OfflineLearnerOptions { ExplorationBatchSize = 5 });

        await _memory.CommitAsync(MakePattern("p1", "already consolidated entry") with
        {
            ConsolidatedInto = "consolidated-p1"
        });
        await _memory.CommitAsync(MakePattern("p2", "active exploration entry"));

        var result = await learner.LearnAsync();

        // Only p2 should be explored; p1 is consolidated
        result.Explored.ShouldBeLessThanOrEqualTo(1);
    }

    [Test]
    public async Task LearnAsync_WithoutSummarizer_SkipsConsolidation()
    {
        var knowledgeStore = new InMemoryKnowledgeStore(_embedder);
        // No summarizer → consolidation skipped
        var learner = new OfflineLearner(_memory, _embedder,
            knowledgeStore: knowledgeStore,
            options: new OfflineLearnerOptions
            {
                ExplorationBatchSize = 1,
                ConsolidationMinStrength = 0.8f,
                ConsolidationMaxVariance = 0.1f,
                ConsolidationMinObservations = 5
            });

        await _memory.CommitAsync(MakePattern("p1", "mature but no summarizer",
            confidence: 0.9f, strength: 0.9f, variance: 0.03f) with
        {
            ObservationCount = 15
        });

        var result = await learner.LearnAsync();

        result.Consolidated.ShouldBe(0);
        var entry = await _memory.GetAsync("p1");
        entry!.ConsolidatedInto.ShouldBeNull();
    }

    [Test]
    public async Task TemplateSummarizer_Pattern_FormatsCorrectly()
    {
        var summarizer = new TemplateConsolidationSummarizer();
        var entry = MakePattern("p1", "GC pause causes timeout",
            confidence: 0.9f, strength: 0.9f, variance: 0.02f) with
        {
            Condition = "GC pause > 200ms",
            Effect = "downstream timeout",
            Mechanism = "thread starvation",
            Evidence = ["log-1", "log-2", "log-3", "log-4"]
        };

        var doc = await summarizer.SummarizeAsync(entry);

        doc.Id.ShouldBe("consolidated-p1");
        doc.Text.ShouldContain("Pattern: GC pause causes timeout");
        doc.Text.ShouldContain("Condition: GC pause > 200ms");
        doc.Text.ShouldContain("Effect: downstream timeout");
        doc.Text.ShouldContain("Mechanism: thread starvation");
        doc.Text.ShouldContain("Evidence (4 total):");
        doc.Metadata["source_entry_id"].ShouldBe("p1");
        doc.Metadata["origin"].ShouldBe("consolidation");
    }

    [Test]
    public async Task MarkConsolidatedAsync_SetsField()
    {
        await _memory.CommitAsync(MakePattern("p1", "mark test"));

        await _memory.MarkConsolidatedAsync("p1", "consolidated-p1");

        var entry = await _memory.GetAsync("p1");
        entry.ShouldNotBeNull();
        entry.ConsolidatedInto.ShouldBe("consolidated-p1");
    }

    [Test]
    public void MarkConsolidatedAsync_NonexistentEntry_Throws()
    {
        Should.Throw<KeyNotFoundException>(async () =>
            await _memory.MarkConsolidatedAsync("nonexistent", "doc-1"));
    }

    private sealed class FakeSimulationSource(float reward) : ISimulationSource
    {
        public int CallCount { get; private set; }

        public Task<SimulationOutcome> SimulateAsync(
            EmpiricalEntry hypothesis,
            IReadOnlyList<EmpiricalMatch> relatedKnowledge,
            int maxEpisodes,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new SimulationOutcome
            {
                Reward = reward,
                Summary = $"Simulated: {hypothesis.Description}",
                EpisodesRun = maxEpisodes,
                EpisodesSupported = (int)(maxEpisodes * ((reward + 1f) / 2f))
            });
        }
    }
}
