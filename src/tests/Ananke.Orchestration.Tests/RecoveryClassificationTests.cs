using System.Diagnostics.Metrics;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class RecoveryClassificationTests
{
    // ── Faulted tool in a successful run → RecoveredFaults ───────────────────

    [Test]
    public async Task FaultedTool_SuccessfulRun_ClassifiedAsRecovered()
    {
        var snapshots = new List<TrajectorySnapshot>();
        var observer = new CapturingObserver(snapshots);

        var model = new SequencedToolModel(
            new AgentResponse
            {
                Text = string.Empty,
                ToolCalls = [new AgentToolCall("c1", "error_tool", "{}")]
            },
            new AgentResponse { Text = "done" }
        );

        var kit = new ToolKit("ops")
            .AddTool("error_tool", "Fails", () => ToolResult.Error("boom"));

        var agent = AgentJobFactory.Create<string>("recovered", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithTrajectoryObserver(observer)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        snapshots.Count.ShouldBe(1);
        var snap = snapshots[0];
        snap.RecoveredFaults.ShouldBe(1);
        snap.AbandonedFaults.ShouldBe(0);
        snap.Succeeded.ShouldBeTrue();
    }

    // ── Faulted tool in a failed run → AbandonedFaults ───────────────────────

    [Test]
    public async Task FaultedTool_FailedRun_ClassifiedAsAbandoned()
    {
        var snapshots = new List<TrajectorySnapshot>();
        var observer = new CapturingObserver(snapshots);

        // Model always requests error_tool and never produces text → job throws
        var model = new AlwaysToolModel("c1", "error_tool", "{}");

        var kit = new ToolKit("ops")
            .AddTool("error_tool", "Fails", () => ToolResult.Error("boom"));

        var agent = AgentJobFactory.Create<string>("abandoned", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithMaxToolRounds(2)
            .WithTrajectoryObserver(observer)
            .MapResult((_, text) => text)
            .Build();

        await Should.ThrowAsync<InvalidOperationException>(() => agent.ExecuteAsync("go"));

        snapshots.Count.ShouldBe(1);
        var snap = snapshots[0];
        snap.AbandonedFaults.ShouldBeGreaterThan(0);
        snap.RecoveredFaults.ShouldBe(0);
        snap.Succeeded.ShouldBeFalse();
    }

    // ── FaultRecovered counter is incremented ────────────────────────────────

    [Test]
    public async Task FaultedTool_SuccessfulRun_IncrementsFaultRecoveredCounter()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "ananke.tools.fault_recovered")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        var snapshots = new List<TrajectorySnapshot>();
        var model = new SequencedToolModel(
            new AgentResponse
            {
                Text = string.Empty,
                ToolCalls = [new AgentToolCall("c1", "error_tool", "{}")]
            },
            new AgentResponse { Text = "done" }
        );

        var kit = new ToolKit("ops")
            .AddTool("error_tool", "Fails", () => ToolResult.Error("boom"));

        var agent = AgentJobFactory.Create<string>("counter-rec", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithTrajectoryObserver(new CapturingObserver(snapshots))
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        measurements.Sum().ShouldBe(1L);
    }

    // ── FaultAbandoned counter is incremented ────────────────────────────────

    [Test]
    public async Task FaultedTool_FailedRun_IncrementsFaultAbandonedCounter()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "ananke.tools.fault_abandoned")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        var snapshots = new List<TrajectorySnapshot>();
        var model = new AlwaysToolModel("c1", "error_tool", "{}");

        var kit = new ToolKit("ops")
            .AddTool("error_tool", "Fails", () => ToolResult.Error("boom"));

        var agent = AgentJobFactory.Create<string>("counter-abn", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithMaxToolRounds(2)
            .WithTrajectoryObserver(new CapturingObserver(snapshots))
            .MapResult((_, text) => text)
            .Build();

        await Should.ThrowAsync<InvalidOperationException>(() => agent.ExecuteAsync("go"));

        measurements.Sum().ShouldBeGreaterThan(0L);
    }

    // ── No faults → neither counter touched ──────────────────────────────────

    [Test]
    public async Task NoFaults_NeitherRecoveredNorAbandonedPopulated()
    {
        var snapshots = new List<TrajectorySnapshot>();
        var observer = new CapturingObserver(snapshots);

        var model = new SequencedToolModel(
            new AgentResponse
            {
                Text = string.Empty,
                ToolCalls = [new AgentToolCall("c1", "real_tool", "{}")]
            },
            new AgentResponse { Text = "done" }
        );

        var kit = new ToolKit("ops")
            .AddTool("real_tool", "Works", () => ToolResult.Ok("ok"));

        var agent = AgentJobFactory.Create<string>("no-fault", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithTrajectoryObserver(observer)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        snapshots[0].RecoveredFaults.ShouldBe(0);
        snapshots[0].AbandonedFaults.ShouldBe(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingObserver(List<TrajectorySnapshot> snapshots) : ITrajectoryObserver
    {
        public ValueTask OnTrajectoryCompleteAsync(TrajectorySnapshot snapshot, CancellationToken ct = default)
        {
            snapshots.Add(snapshot);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequencedToolModel(params AgentResponse[] responses) : IAgentModel
    {
        private readonly Queue<AgentResponse> _queue = new(responses);

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(_queue.Count > 1 ? _queue.Dequeue() : _queue.Peek());
    }

    /// <summary>Always requests the same tool call — drives the agent past the max-rounds limit.</summary>
    private sealed class AlwaysToolModel(string callId, string toolName, string args) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                Text = string.Empty,
                ToolCalls = [new AgentToolCall(callId, toolName, args)]
            });
    }
}
