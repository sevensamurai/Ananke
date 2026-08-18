using Ananke.Learning.EmpiricalMemory;
using Shouldly;

namespace Ananke.Qdrant.Tests;

/// <summary>
/// Unit tests for <see cref="QdrantEmpiricalMemory"/>'s pure payload mapping —
/// <see cref="QdrantEmpiricalMemory.BuildPoint"/> and
/// <see cref="QdrantEmpiricalMemory.MapPayloadToEntry"/> — the part of the class that is
/// Ananke's own responsibility, as opposed to Qdrant's behavior. No Qdrant connection or
/// Docker is needed: both methods are plain functions over an <see cref="EmpiricalEntry"/>
/// and a payload dictionary. Mirrors the pattern established by
/// <c>Ananke.Redis.Tests.RedisCheckpointStoreTests</c>.
/// </summary>
[TestFixture]
public class QdrantEmpiricalMemoryPayloadTests
{
    private static readonly ReadOnlyMemory<float> Embedding = new float[] { 0.1f, 0.2f, 0.3f };

    [Test]
    public void RoundTrip_EpisodeIdAndStepIndex_Present_PreservesBoth()
    {
        // Pins Q17: EpisodeId/StepIndex were dropped entirely — written to neither the
        // payload nor read back — so any entry committed through Qdrant lost its episode
        // linkage. Fixed 2026-08-03; this is the first automated test for that fix.
        var entry = MinimalEntry() with { EpisodeId = "episode-42", StepIndex = 3 };

        var roundTripped = RoundTrip(entry);

        roundTripped.EpisodeId.ShouldBe("episode-42");
        roundTripped.StepIndex.ShouldBe(3);
    }

    [Test]
    public void RoundTrip_EpisodeIdAndStepIndex_Absent_StaysNull()
    {
        var entry = MinimalEntry() with { EpisodeId = null, StepIndex = null };

        var roundTripped = RoundTrip(entry);

        roundTripped.EpisodeId.ShouldBeNull();
        roundTripped.StepIndex.ShouldBeNull();
    }

    [Test]
    public void RoundTrip_StepIndexZero_IsDistinctFromAbsent()
    {
        // StepIndex 0 is a real, meaningful value (the first step of an episode) — must not
        // collapse to "absent" the way a naive `if (entry.StepIndex > 0)` guard would.
        var entry = MinimalEntry() with { EpisodeId = "episode-1", StepIndex = 0 };

        var roundTripped = RoundTrip(entry);

        roundTripped.StepIndex.ShouldBe(0);
    }

    [Test]
    public void RoundTrip_SubMinuteLatency_PreservesSecondPrecision()
    {
        // Pins Q18: Latency was truncated to whole minutes (`TotalMinutes` cast to long), so
        // a 90-second condition-to-effect delay persisted as exactly 1 minute and sub-minute
        // latencies became 0. Fixed 2026-08-06 by switching the payload key from
        // latency_minutes to latency_seconds; this is the first automated test for that fix.
        var entry = MinimalEntry() with { Latency = TimeSpan.FromSeconds(90) };

        var roundTripped = RoundTrip(entry);

        roundTripped.Latency.ShouldBe(TimeSpan.FromSeconds(90));
        roundTripped.Latency.ShouldNotBe(TimeSpan.FromMinutes(1));
    }

    [Test]
    public void RoundTrip_CoreScalarFields_SurviveUnchanged()
    {
        var entry = MinimalEntry() with
        {
            Confidence = 0.73f,
            ObservationCount = 5,
            Tags = ["alpha", "beta"],
            Evidence = ["log-1", "log-2"],
            Source = "human-confirmed",
            Condition = "high load",
            Effect = "latency spike"
        };

        var roundTripped = RoundTrip(entry);

        roundTripped.Confidence.ShouldBe(0.73f, tolerance: 0.0001f);
        roundTripped.ObservationCount.ShouldBe(5);
        roundTripped.Tags.ShouldBe(["alpha", "beta"]);
        roundTripped.Evidence.ShouldBe(["log-1", "log-2"]);
        roundTripped.Source.ShouldBe("human-confirmed");
        roundTripped.Condition.ShouldBe("high load");
        roundTripped.Effect.ShouldBe("latency spike");
    }

    private static EmpiricalEntry RoundTrip(EmpiricalEntry entry)
    {
        var point = QdrantEmpiricalMemory.BuildPoint(entry, Embedding);
        return QdrantEmpiricalMemory.MapPayloadToEntry(entry.Id, point.Payload);
    }

    private static EmpiricalEntry MinimalEntry() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Kind = EmpiricalKind.Pattern,
        Tags = [],
        Source = "auto-detected",
        Description = SemanticDescription.FromText("a test entry"),
        Confidence = 0.5f,
        ObservationCount = 1,
        Evidence = [],
        FirstObserved = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        LastObserved = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
    };
}
