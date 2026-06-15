using Ananke.Abstractions.Trajectory;
using Shouldly;

namespace Ananke.Abstractions.Tests.Trajectory;

[TestFixture]
public class TrajectorySnapshotTests
{
    // ── ToolEfficiency ────────────────────────────────────────────────────────

    [Test]
    public void ToolEfficiency_AllSuccessful_ReturnsOne()
    {
        var s = Snapshot(total: 4, successful: 4);
        s.ToolEfficiency.ShouldBe(1f);
    }

    [Test]
    public void ToolEfficiency_HalfSuccessful_ReturnsHalf()
    {
        var s = Snapshot(total: 4, successful: 2);
        s.ToolEfficiency.ShouldBe(0.5f);
    }

    [Test]
    public void ToolEfficiency_AllHallucinated_ReturnsZero()
    {
        var s = Snapshot(total: 3, successful: 0, hallucinated: 3);
        s.ToolEfficiency.ShouldBe(0f);
    }

    [Test]
    public void ToolEfficiency_ZeroToolCalls_ReturnsZero()
    {
        var s = Snapshot(total: 0);
        s.ToolEfficiency.ShouldBe(0f);
    }

    // ── RecoveryRate ──────────────────────────────────────────────────────────

    [Test]
    public void RecoveryRate_AllRecovered_ReturnsOne()
    {
        var s = Snapshot(recoveredFaults: 3, abandonedFaults: 0);
        s.RecoveryRate.ShouldBe(1f);
    }

    [Test]
    public void RecoveryRate_HalfRecovered_ReturnsHalf()
    {
        var s = Snapshot(recoveredFaults: 2, abandonedFaults: 2);
        s.RecoveryRate.ShouldBe(0.5f);
    }

    [Test]
    public void RecoveryRate_NoFaults_ReturnsZero()
    {
        var s = Snapshot(recoveredFaults: 0, abandonedFaults: 0);
        s.RecoveryRate.ShouldBe(0f);
    }

    // ── CostPerSuccessfulTrajectory semantics ─────────────────────────────────

    [Test]
    public void Snapshot_FailedTrajectory_CostPerSuccessIsZero()
    {
        var s = Snapshot() with { Succeeded = false, CostPerSuccessfulTrajectory = 0m };
        s.CostPerSuccessfulTrajectory.ShouldBe(0m);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TrajectorySnapshot Snapshot(
        int total = 0,
        int successful = 0,
        int hallucinated = 0,
        int faulted = 0,
        int recoveredFaults = 0,
        int abandonedFaults = 0) =>
        new()
        {
            EpisodeId = "test",
            CapturedAt = DateTimeOffset.UtcNow,
            TotalToolCalls = total,
            SuccessfulToolCalls = successful,
            HallucinatedToolCalls = hallucinated,
            FaultedToolCalls = faulted,
            RecoveredFaults = recoveredFaults,
            AbandonedFaults = abandonedFaults,
        };
}
