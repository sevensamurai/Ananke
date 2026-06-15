using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Agents.Trajectory;
using Ananke.Orchestration.Tools.Gating;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class CompositeAdaptivePolicyTests
{
    // ── Penalty applied to kit tools on abandoned faults ─────────────────────

    [Test]
    public async Task AbandonedFaults_AppliesPenaltyToTrackedKitTools()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("ops", "tool_a", 0.5f);
        tracker.RecordOutcome("ops", "tool_b", 0.5f);

        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions { KitName = "ops", AbandonedFaultPenalty = -0.8f });

        var snapshot = BuildSnapshot(succeeded: false, abandonedFaults: 1);
        await policy.AdaptAsync(snapshot);

        var affinities = tracker.GetAffinities();
        affinities["ops::tool_a"].MeanReward.ShouldBeLessThan(0.5f);
        affinities["ops::tool_b"].MeanReward.ShouldBeLessThan(0.5f);
    }

    // ── Reward applied to kit tools on clean success ──────────────────────────

    [Test]
    public async Task CleanSuccess_AppliersRewardToTrackedKitTools()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("ops", "tool_a", 0.0f);

        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions { KitName = "ops", SuccessReward = 1.0f });

        var snapshot = BuildSnapshot(succeeded: true, retryCount: 0);
        await policy.AdaptAsync(snapshot);

        var affinities = tracker.GetAffinities();
        affinities["ops::tool_a"].MeanReward.ShouldBeGreaterThan(0.0f);
    }

    // ── Success with retries does NOT trigger reward rule ────────────────────

    [Test]
    public async Task SuccessWithRetries_DoesNotApplyReward()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("ops", "tool_a", 0.0f);
        var initialMean = tracker.GetAffinities()["ops::tool_a"].MeanReward;

        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions { KitName = "ops", SuccessReward = 1.0f });

        var snapshot = BuildSnapshot(succeeded: true, retryCount: 2);
        await policy.AdaptAsync(snapshot);

        tracker.GetAffinities()["ops::tool_a"].MeanReward.ShouldBe(initialMean);
    }

    // ── Hallucination threshold triggers ILearningCycleTrigger ───────────────

    [Test]
    public async Task HallucinationsAboveThreshold_TriggerLearningCycle()
    {
        var tracker = new ToolAffinityTracker();
        var trigger = new CapturingLearningTrigger();

        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions { HallucinationThreshold = 2 },
            learningTrigger: trigger);

        var snapshot = BuildSnapshot(hallucinatedToolCalls: 3);
        await policy.AdaptAsync(snapshot);

        trigger.TriggerCount.ShouldBe(1);
    }

    // ── Hallucinations below threshold does NOT trigger learning cycle ────────

    [Test]
    public async Task HallucinationsBelowThreshold_DoesNotTriggerLearningCycle()
    {
        var tracker = new ToolAffinityTracker();
        var trigger = new CapturingLearningTrigger();

        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions { HallucinationThreshold = 5 },
            learningTrigger: trigger);

        var snapshot = BuildSnapshot(hallucinatedToolCalls: 2);
        await policy.AdaptAsync(snapshot);

        trigger.TriggerCount.ShouldBe(0);
    }

    // ── No tracked tools → outcome rules are no-ops ──────────────────────────

    [Test]
    public async Task NoTrackedTools_OutcomeRulesDoNotThrow()
    {
        var tracker = new ToolAffinityTracker();
        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions { KitName = "empty", AbandonedFaultPenalty = -0.8f });

        var snapshot = BuildSnapshot(succeeded: false, abandonedFaults: 2);
        await Should.NotThrowAsync(() => policy.AdaptAsync(snapshot).AsTask());
    }

    // ── ITrajectoryObserver delegates to AdaptAsync ──────────────────────────

    [Test]
    public async Task OnTrajectoryCompleteAsync_DelegatesToAdaptAsync()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("ops", "tool_a", 0.0f);
        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions { KitName = "ops", SuccessReward = 1.0f });

        ITrajectoryObserver observer = policy;
        var snapshot = BuildSnapshot(succeeded: true, retryCount: 0);
        await observer.OnTrajectoryCompleteAsync(snapshot);

        tracker.GetAffinities()["ops::tool_a"].MeanReward.ShouldBeGreaterThan(0.0f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TrajectorySnapshot BuildSnapshot(
        bool succeeded = true,
        int retryCount = 0,
        int abandonedFaults = 0,
        int recoveredFaults = 0,
        int hallucinatedToolCalls = 0)
        => new()
        {
            EpisodeId = Guid.NewGuid().ToString("N"),
            CapturedAt = DateTimeOffset.UtcNow,
            Succeeded = succeeded,
            RetryCount = retryCount,
            AbandonedFaults = abandonedFaults,
            RecoveredFaults = recoveredFaults,
            HallucinatedToolCalls = hallucinatedToolCalls,
            TotalToolCalls = 0,
            SuccessfulToolCalls = 0,
            FaultedToolCalls = abandonedFaults + recoveredFaults,
            TotalCost = 0m,
            CostPerSuccessfulTrajectory = 0m,
            Duration = TimeSpan.FromSeconds(1),
        };

    private sealed class CapturingLearningTrigger : ILearningCycleTrigger
    {
        public int TriggerCount { get; private set; }

        public Task TriggerAsync(CancellationToken ct = default)
        {
            TriggerCount++;
            return Task.CompletedTask;
        }
    }
}
