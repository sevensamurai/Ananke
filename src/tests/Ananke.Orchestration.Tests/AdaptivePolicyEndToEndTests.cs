using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Trajectory;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Gating;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Verifies the full TextAgentJob → TrajectorySnapshot → IAdaptiveHarnessPolicy chain.
/// </summary>
[TestFixture]
public class AdaptivePolicyEndToEndTests
{
    // ── Successful agent run triggers reward on CompositeAdaptiveHarnessPolicy ─

    [Test]
    public async Task SuccessfulRun_PolicyReceivesSnapshot_AppliesReward()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("ops", "tool_a", 0.0f);

        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions
            {
                KitName = "ops",
                SuccessReward = 1.0f,
                HallucinationThreshold = 10,
            });

        var model = new FixedTextModel("done");
        var agent = AgentJobFactory.Create<string>("e2e", model)
            .WithPrompt(s => s)
            .WithTrajectoryObserver(policy)
            .MapResult((_, text) => text)
            .Build();

        var result = await agent.ExecuteAsync("go");
        result.ShouldBe("done");

        // Policy applied reward to the tool tracked under the "ops" kit
        tracker.GetAffinities()["ops::tool_a"].MeanReward.ShouldBeGreaterThan(0.0f);
    }

    // ── Faulted tool in failed run triggers penalty ───────────────────────────

    [Test]
    public async Task FailedRunWithFaults_PolicyReceivesSnapshot_AppliesPenalty()
    {
        var tracker = new ToolAffinityTracker();
        tracker.RecordOutcome("ops", "tool_a", 0.5f);
        var initialMean = tracker.GetAffinities()["ops::tool_a"].MeanReward;

        var snapshots = new List<TrajectorySnapshot>();
        var policy = new CompositeAdaptiveHarnessPolicy(tracker,
            new AdaptiveHarnessOptions
            {
                KitName = "ops",
                AbandonedFaultPenalty = -0.8f,
            });

        // Model always requests error_tool and never produces text → exceeds max rounds
        var model = new AlwaysToolModel("c1", "error_tool", "{}");
        var kit = new ToolKit("ops")
            .AddTool("error_tool", "Fails", () => ToolResult.Error("boom"));

        var agent = AgentJobFactory.Create<string>("e2e-fault", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithMaxToolRounds(2)
            .WithTrajectoryObserver(policy)
            .MapResult((_, text) => text)
            .Build();

        await Should.ThrowAsync<InvalidOperationException>(() => agent.ExecuteAsync("go"));

        tracker.GetAffinities()["ops::tool_a"].MeanReward.ShouldBeLessThan(initialMean);
    }

    // ── NullAdaptiveHarnessPolicy does nothing, no crash ─────────────────────

    [Test]
    public async Task NullPolicy_RunsWithoutSideEffects()
    {
        var model = new FixedTextModel("ok");
        var agent = AgentJobFactory.Create<string>("null-policy", model)
            .WithPrompt(s => s)
            .WithTrajectoryObserver(new NullTrajectoryObserverAdapter())
            .MapResult((_, text) => text)
            .Build();

        var result = await agent.ExecuteAsync("go");
        result.ShouldBe("ok");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class FixedTextModel(string text) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = text });
    }

    private sealed class AlwaysToolModel(string callId, string toolName, string args) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                Text = string.Empty,
                ToolCalls = [new AgentToolCall(callId, toolName, args)]
            });
    }

    private sealed class NullTrajectoryObserverAdapter : ITrajectoryObserver
    {
        public ValueTask OnTrajectoryCompleteAsync(TrajectorySnapshot snapshot, CancellationToken ct = default)
            => NullAdaptiveHarnessPolicy.Instance.AdaptAsync(snapshot, ct);
    }
}
