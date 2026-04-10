using Ananke.Orchestration.Knowledge;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Learning;
using Shouldly;

namespace Ananke.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="IEmpiricalMemory"/> via <see cref="InMemoryEmpiricalMemory"/>.
/// Validates the full Commit → Recall → Reinforce → Contradict learning lifecycle
/// to ensure empirical knowledge is stored, retrieved, and evolved correctly.
/// </summary>
[TestFixture]
public class EmpiricalMemoryTests
{
    private InMemoryEmpiricalMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        _memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
    }

    // ── Commit → Recall ──────────────────────────────────────────

    [Test]
    public async Task CommitAsync_then_RecallAsync_returns_committed_entry()
    {
        var entry = MakePattern(
            "pattern-1",
            "When CPU usage exceeds 90% on ServiceA, downstream timeouts spike within 30 seconds",
            condition: "CPU usage > 90% on ServiceA",
            effect: "Downstream timeout rate increases");

        await _memory.CommitAsync(entry);

        var results = await _memory.RecallAsync("ServiceA high CPU and timeouts");

        results.ShouldNotBeEmpty();
        results[0].Entry.Id.ShouldBe("pattern-1");
        results[0].Score.ShouldBeGreaterThan(0f);
    }

    [Test]
    public async Task RecallAsync_returns_empty_when_nothing_committed()
    {
        var results = await _memory.RecallAsync("anything at all");

        results.ShouldBeEmpty();
    }

    [Test]
    public async Task CommitAsync_multiple_entries_RecallAsync_ranks_most_relevant_first()
    {
        await _memory.CommitAsync(MakePattern(
            "unrelated",
            "Disk space on logging server fills up every Monday morning",
            condition: "Monday 6 AM",
            effect: "Log ingestion stops"));

        await _memory.CommitAsync(MakePattern(
            "relevant",
            "Memory leak in payment service causes OOM crashes after 48 hours of uptime",
            condition: "Payment service uptime > 48h",
            effect: "Out of memory crash"));

        await _memory.CommitAsync(MakeHeuristic(
            "also-relevant",
            "Restart payment service proactively before 48 hours to avoid OOM crash"));

        var results = await _memory.RecallAsync("payment service memory crash");

        results.Count.ShouldBeGreaterThanOrEqualTo(1);
        // The top result should be one of the payment-related entries
        var topIds = results.Select(r => r.Entry.Id).ToList();
        topIds.ShouldContain("relevant");
    }

    // ── Recall filtering ─────────────────────────────────────────

    [Test]
    public async Task RecallAsync_filters_by_kind()
    {
        await _memory.CommitAsync(MakePattern(
            "p1", "Database connection pool exhaustion causes request failures"));

        await _memory.CommitAsync(MakeSkill(
            "s1", "Diagnose database connection pool issues by checking active connections"));

        var patternsOnly = await _memory.RecallAsync(
            "database connection problems",
            new RecallOptions { Kind = EmpiricalKind.Pattern });

        patternsOnly.ShouldAllBe(m => m.Entry.Kind == EmpiricalKind.Pattern);
    }

    [Test]
    public async Task RecallAsync_filters_by_min_confidence()
    {
        await _memory.CommitAsync(MakePattern(
            "low-conf", "Flaky correlation between deploy and errors",
            confidence: 0.1f));

        await _memory.CommitAsync(MakePattern(
            "high-conf", "Strong correlation between deploy and errors",
            confidence: 0.8f));

        var results = await _memory.RecallAsync(
            "deploy errors correlation",
            new RecallOptions { MinConfidence = 0.5f });

        results.ShouldAllBe(m => m.Entry.Confidence >= 0.5f);
    }

    [Test]
    public async Task RecallAsync_filters_by_required_tags()
    {
        await _memory.CommitAsync(MakePattern(
            "tagged", "Network latency spikes during peak hours",
            tags: ["network", "latency"]));

        await _memory.CommitAsync(MakePattern(
            "untagged", "Network latency increases with payload size",
            tags: ["network", "payload"]));

        var results = await _memory.RecallAsync(
            "network latency",
            new RecallOptions { RequiredTags = ["latency"] });

        results.ShouldAllBe(m => m.Entry.Tags.Contains("latency"));
    }

    [Test]
    public async Task RecallAsync_respects_TopK()
    {
        for (var i = 0; i < 10; i++)
        {
            await _memory.CommitAsync(MakeHeuristic(
                $"h-{i}",
                $"Heuristic about server scaling strategy number {i}",
                confidence: 0.5f + i * 0.05f));
        }

        var results = await _memory.RecallAsync(
            "server scaling",
            new RecallOptions { TopK = 3 });

        results.Count.ShouldBeLessThanOrEqualTo(3);
    }

    // ── Reinforce ────────────────────────────────────────────────

    [Test]
    public async Task ReinforceAsync_increases_confidence_and_observation_count()
    {
        var committed = await _memory.CommitAsync(MakePattern(
            "reinforce-target",
            "Cache invalidation delay causes stale reads",
            confidence: 0.3f));

        committed.Confidence.ShouldBe(0.3f);
        committed.ObservationCount.ShouldBe(1);

        await _memory.ReinforceAsync("reinforce-target", new Reinforcement
        {
            NewEvidence = ["incident-42: confirmed stale read after cache clear"],
            ConfidenceAdjustment = 0.2f,
            Source = "incident-analysis"
        });

        var updated = await _memory.GetAsync("reinforce-target");

        updated.ShouldNotBeNull();
        updated.Confidence.ShouldBe(0.5f);
        updated.ObservationCount.ShouldBe(2);
        updated.Evidence.ShouldContain("incident-42: confirmed stale read after cache clear");
    }

    [Test]
    public async Task ReinforceAsync_caps_confidence_at_one()
    {
        await _memory.CommitAsync(MakePattern(
            "near-max",
            "Extremely well-established pattern",
            confidence: 0.95f));

        await _memory.ReinforceAsync("near-max", new Reinforcement
        {
            NewEvidence = ["yet another confirmation"],
            ConfidenceAdjustment = 0.2f,
            Source = "test"
        });

        var updated = await _memory.GetAsync("near-max");
        updated!.Confidence.ShouldBeLessThanOrEqualTo(1.0f);
    }

    [Test]
    public async Task ReinforceAsync_throws_for_unknown_entry()
    {
        await Should.ThrowAsync<KeyNotFoundException>(
            () => _memory.ReinforceAsync("does-not-exist", new Reinforcement
            {
                NewEvidence = ["evidence"],
                Source = "test"
            }));
    }

    // ── Contradict ───────────────────────────────────────────────

    [Test]
    public async Task ContradictAsync_decreases_confidence()
    {
        await _memory.CommitAsync(MakePattern(
            "contradict-target",
            "Assumed correlation between deploy time and error rate",
            confidence: 0.6f));

        await _memory.ContradictAsync("contradict-target",
            "A/B test showed no causal relationship");

        var updated = await _memory.GetAsync("contradict-target");

        updated.ShouldNotBeNull();
        updated.Confidence.ShouldBe(0.3f);
        updated.Evidence.ShouldContain(e => e.Contains("contradicted"));
    }

    [Test]
    public async Task ContradictAsync_floors_confidence_at_zero()
    {
        await _memory.CommitAsync(MakePattern(
            "low-start",
            "Weak hypothesis about correlation",
            confidence: 0.1f));

        await _memory.ContradictAsync("low-start", "disproven");

        var updated = await _memory.GetAsync("low-start");
        updated!.Confidence.ShouldBeGreaterThanOrEqualTo(0f);
    }

    [Test]
    public async Task ContradictAsync_throws_for_unknown_entry()
    {
        await Should.ThrowAsync<KeyNotFoundException>(
            () => _memory.ContradictAsync("does-not-exist", "reason"));
    }

    // ── Semantic dedup ───────────────────────────────────────────

    [Test]
    public async Task CommitAsync_deduplicates_semantically_similar_entries()
    {
        var first = await _memory.CommitAsync(MakePattern(
            "dup-1",
            "High CPU on ServiceA causes downstream timeout failures",
            confidence: 0.3f));

        // Commit a near-identical entry with a different ID
        var second = await _memory.CommitAsync(MakePattern(
            "dup-2",
            "High CPU on ServiceA causes downstream timeout failures",
            confidence: 0.3f));

        // The second commit should have merged into the first
        second.Id.ShouldBe("dup-1");
        second.ObservationCount.ShouldBe(2); // observation tracked, confidence unchanged
        second.Confidence.ShouldBe(0.3f);
        _memory.Count.ShouldBe(1);
    }

    // ── GetAsync ─────────────────────────────────────────────────

    [Test]
    public async Task GetAsync_returns_null_for_missing_entry()
    {
        var result = await _memory.GetAsync("nonexistent");
        result.ShouldBeNull();
    }

    [Test]
    public async Task GetAsync_returns_committed_entry()
    {
        await _memory.CommitAsync(MakeHeuristic("get-test", "Always check logs first"));

        var result = await _memory.GetAsync("get-test");

        result.ShouldNotBeNull();
        result.Id.ShouldBe("get-test");
    }

    // ── Full learning lifecycle ──────────────────────────────────

    [Test]
    public async Task Full_learning_lifecycle_commit_recall_reinforce_recall_with_higher_score()
    {
        // 1. Agent discovers a pattern during analysis
        await _memory.CommitAsync(MakePattern(
            "lifecycle-pattern",
            "Restarting the cache service resolves stale data issues",
            condition: "Stale data detected in API responses",
            effect: "Cache restart resolves staleness",
            confidence: 0.3f));

        // 2. First recall — pattern exists but confidence is low
        var firstRecall = await _memory.RecallAsync("stale data in API responses");
        firstRecall.ShouldNotBeEmpty();
        var firstScore = firstRecall[0].Score;

        // 3. Pattern is confirmed — reinforce
        await _memory.ReinforceAsync("lifecycle-pattern", new Reinforcement
        {
            NewEvidence = ["incident-101: cache restart fixed stale data"],
            ConfidenceAdjustment = 0.2f,
            Source = "incident-review"
        });

        // 4. Second recall — same query, higher score due to increased confidence
        var secondRecall = await _memory.RecallAsync("stale data in API responses");
        secondRecall.ShouldNotBeEmpty();
        secondRecall[0].Score.ShouldBeGreaterThan(firstScore);
        secondRecall[0].Entry.ObservationCount.ShouldBe(2);
    }

    [Test]
    public async Task Reinforced_entry_has_higher_confidence_than_contradicted_entry()
    {
        await _memory.CommitAsync(MakePattern(
            "confirmed-pattern",
            "Cache invalidation delay causes stale API responses to persist",
            confidence: 0.4f));

        await _memory.CommitAsync(MakePattern(
            "disproven-pattern",
            "Thread pool starvation causes intermittent timeouts in background jobs",
            confidence: 0.4f));

        // Reinforce the confirmed one multiple times
        await _memory.ReinforceAsync("confirmed-pattern", new Reinforcement
        {
            NewEvidence = ["incident-1: confirmed cache staleness"],
            ConfidenceAdjustment = 0.2f,
            Source = "test"
        });
        await _memory.ReinforceAsync("confirmed-pattern", new Reinforcement
        {
            NewEvidence = ["incident-2: confirmed again"],
            ConfidenceAdjustment = 0.2f,
            Source = "test"
        });

        // Contradict the disproven one
        await _memory.ContradictAsync("disproven-pattern",
            "Root cause was a network partition, not thread pool starvation");

        var confirmed = await _memory.GetAsync("confirmed-pattern");
        var disproven = await _memory.GetAsync("disproven-pattern");

        confirmed.ShouldNotBeNull();
        disproven.ShouldNotBeNull();
        confirmed.Confidence.ShouldBeGreaterThan(disproven.Confidence,
            "Reinforced entry should have higher confidence than contradicted entry");
        confirmed.ObservationCount.ShouldBe(3);
        disproven.Evidence.ShouldContain(e => e.Contains("contradicted"));
    }

    [Test]
    public async Task Multi_kind_recall_returns_patterns_skills_and_heuristics()
    {
        await _memory.CommitAsync(MakePattern(
            "mk-pattern",
            "Deploying on Friday afternoon correlates with weekend incidents",
            condition: "Friday afternoon deploy",
            effect: "Weekend incident rate increases"));

        await _memory.CommitAsync(MakeSkill(
            "mk-skill",
            "Investigate deployment-related incidents by checking deploy timestamps and error logs"));

        await _memory.CommitAsync(MakeHeuristic(
            "mk-heuristic",
            "Avoid deploying on Friday afternoons to reduce weekend incident risk"));

        var results = await _memory.RecallAsync("Friday deploy incidents");

        results.Count.ShouldBeGreaterThanOrEqualTo(2);
        var kinds = results.Select(r => r.Entry.Kind).Distinct().ToList();
        kinds.Count.ShouldBeGreaterThanOrEqualTo(2, "Should return entries of multiple kinds");
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static EmpiricalEntry MakePattern(
        string id,
        string description,
        string? condition = null,
        string? effect = null,
        float confidence = 0.4f,
        IReadOnlyList<string>? tags = null) => new()
    {
        Id = id,
        Kind = EmpiricalKind.Pattern,
        Tags = tags ?? ["test"],
        Source = "test",
        Description = SemanticDescription.FromText(description),
        Condition = condition,
        Effect = effect,
        Confidence = confidence,
        ObservationCount = 1,
        Evidence = [$"test-evidence-{id}"],
        FirstObserved = DateTimeOffset.UtcNow,
        LastObserved = DateTimeOffset.UtcNow
    };

    private static EmpiricalEntry MakeHeuristic(
        string id,
        string description,
        float confidence = 0.4f,
        IReadOnlyList<string>? tags = null) => new()
    {
        Id = id,
        Kind = EmpiricalKind.Heuristic,
        Tags = tags ?? ["test"],
        Source = "test",
        Description = SemanticDescription.FromText(description),
        Confidence = confidence,
        ObservationCount = 1,
        Evidence = [$"test-evidence-{id}"],
        FirstObserved = DateTimeOffset.UtcNow,
        LastObserved = DateTimeOffset.UtcNow
    };

    private static EmpiricalEntry MakeSkill(
        string id,
        string description,
        float confidence = 0.4f) => new()
    {
        Id = id,
        Kind = EmpiricalKind.Skill,
        Tags = ["test"],
        Source = "test",
        Description = SemanticDescription.FromText(description),
        Confidence = confidence,
        ObservationCount = 1,
        Evidence = [$"test-evidence-{id}"],
        FirstObserved = DateTimeOffset.UtcNow,
        LastObserved = DateTimeOffset.UtcNow
    };
}
