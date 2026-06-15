using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class TrajectorySnapshotBuilderTests
{
    // ── Snapshot emitted on successful plain run ──────────────────────────────

    [Test]
    public async Task PlainRun_EmitsSucceededSnapshot()
    {
        var snapshots = new List<TrajectorySnapshot>();
        var observer = new CapturingObserver(snapshots);

        var model = SimulatedModel.Fixed("hello");
        var agent = AgentJobFactory.Create<string>("plain", model)
            .WithPrompt(s => s)
            .WithTrajectoryObserver(observer)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        snapshots.Count.ShouldBe(1);
        snapshots[0].Succeeded.ShouldBeTrue();
        snapshots[0].Duration.ShouldBeGreaterThan(TimeSpan.Zero);
        snapshots[0].TotalToolCalls.ShouldBe(0);
    }

    // ── Tool calls are counted correctly ─────────────────────────────────────

    [Test]
    public async Task ToolRun_CountsSuccessfulAndHallucinatedCalls()
    {
        var snapshots = new List<TrajectorySnapshot>();
        var observer = new CapturingObserver(snapshots);

        // Model: call real_tool (success), then ghost_tool (hallucination), then done.
        var model = new SequencedToolModel(
            new AgentResponse
            {
                Text = string.Empty,
                ToolCalls = [new AgentToolCall("c1", "real_tool", "{}")]
            },
            new AgentResponse
            {
                Text = string.Empty,
                ToolCalls = [new AgentToolCall("c2", "ghost_tool", "{}")]
            },
            new AgentResponse { Text = "done" }
        );

        var kit = new ToolKit("ops")
            .AddTool("real_tool", "Real", () => ToolResult.Ok("ok"));

        var agent = AgentJobFactory.Create<string>("counter", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithMaxToolRounds(5)
            .WithTrajectoryObserver(observer)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        snapshots.Count.ShouldBe(1);
        var snap = snapshots[0];
        snap.TotalToolCalls.ShouldBe(2);
        snap.SuccessfulToolCalls.ShouldBe(1);
        snap.HallucinatedToolCalls.ShouldBe(1);
        snap.FaultedToolCalls.ShouldBe(0);
        snap.Succeeded.ShouldBeTrue();
    }

    // ── Faulted tool is counted separately from hallucination ────────────────

    [Test]
    public async Task FaultedTool_CountedAsFaulted_NotHallucinated()
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

        var agent = AgentJobFactory.Create<string>("faults", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithTrajectoryObserver(observer)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        var snap = snapshots[0];
        snap.FaultedToolCalls.ShouldBe(1);
        snap.HallucinatedToolCalls.ShouldBe(0);
        snap.SuccessfulToolCalls.ShouldBe(0);
    }

    // ── No observer means no snapshot, no crash ──────────────────────────────

    [Test]
    public async Task NoObserver_RunsNormally()
    {
        var model = SimulatedModel.Fixed("ok");
        var agent = AgentJobFactory.Create<string>("no-obs", model)
            .WithPrompt(s => s)
            .MapResult((_, text) => text)
            .Build();

        var result = await agent.ExecuteAsync("go");
        result.ShouldBe("ok");
    }

    // ── EpisodeId is stable within a run ─────────────────────────────────────

    [Test]
    public async Task HallucinationEvent_ContainsSameEpisodeIdAsSnapshot()
    {
        var snapshots = new List<TrajectorySnapshot>();
        var events = new List<Ananke.Abstractions.Tools.HallucinatedToolCallEvent>();
        var snapshotObserver = new CapturingObserver(snapshots);
        var halObserver = new CapturingHallucinationObserver(events);

        var model = new SequencedToolModel(
            new AgentResponse
            {
                Text = string.Empty,
                ToolCalls = [new AgentToolCall("c1", "ghost", "{}")]
            },
            new AgentResponse { Text = "done" }
        );

        var kit = new ToolKit("ops")
            .AddTool("real_tool", "Real", () => ToolResult.Ok("ok"))
            .WithHallucinationObserver(halObserver);

        var agent = AgentJobFactory.Create<string>("episode-id", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .WithTrajectoryObserver(snapshotObserver)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        snapshots.Count.ShouldBe(1);
        events.Count.ShouldBe(1);
        events[0].EpisodeId.ShouldBe(snapshots[0].EpisodeId);
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

    private sealed class CapturingHallucinationObserver(
        List<Ananke.Abstractions.Tools.HallucinatedToolCallEvent> events)
        : Ananke.Abstractions.Tools.IHallucinationObserver
    {
        public ValueTask ReportAsync(
            Ananke.Abstractions.Tools.HallucinatedToolCallEvent @event, CancellationToken ct = default)
        {
            events.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SimulatedModel(AgentResponse response) : IAgentModel
    {
        public static SimulatedModel Fixed(string text) =>
            new(new AgentResponse { Text = text });

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(response);
    }

    private sealed class SequencedToolModel(params AgentResponse[] responses) : IAgentModel
    {
        private readonly Queue<AgentResponse> _queue = new(responses);

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(_queue.Count > 1 ? _queue.Dequeue() : _queue.Peek());
    }
}
