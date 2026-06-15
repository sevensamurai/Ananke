using System.Diagnostics.Metrics;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class RetryAttributeTests
{
    // ── ModelRetry counter is incremented on each retry ───────────────────────

    [Test]
    public async Task TransientFailure_IncrementsModelRetryCounter()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "ananke.model.retry")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        var model = new FailThenSucceedModel(failCount: 2, successText: "ok");
        var agent = AgentJobFactory.Create<string>("retry-counter", model)
            .WithPrompt(s => s)
            .WithRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1))
            .MapResult((_, text) => text)
            .Build();

        var result = await agent.ExecuteAsync("go");
        result.ShouldBe("ok");

        measurements.Sum().ShouldBe(2L);
    }

    // ── RetryCount is recorded in the trajectory snapshot ────────────────────

    [Test]
    public async Task TransientFailure_PopulatesRetryCountInSnapshot()
    {
        var snapshots = new List<TrajectorySnapshot>();
        var observer = new CapturingObserver(snapshots);

        var model = new FailThenSucceedModel(failCount: 1, successText: "done");
        var agent = AgentJobFactory.Create<string>("retry-snap", model)
            .WithPrompt(s => s)
            .WithRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1))
            .WithTrajectoryObserver(observer)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        snapshots.Count.ShouldBe(1);
        snapshots[0].RetryCount.ShouldBe(1);
        snapshots[0].Succeeded.ShouldBeTrue();
    }

    // ── No retry on success means counter stays zero ─────────────────────────

    [Test]
    public async Task NoFailure_DoesNotIncrementModelRetryCounter()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "ananke.model.retry")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        var model = new FailThenSucceedModel(failCount: 0, successText: "ok");
        var agent = AgentJobFactory.Create<string>("no-retry", model)
            .WithPrompt(s => s)
            .WithRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1))
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        measurements.ShouldBeEmpty();
    }

    // ── Exhausting retries throws AggregateException ─────────────────────────

    [Test]
    public async Task AllAttemptsExhausted_ThrowsAggregateException()
    {
        var model = new AlwaysFailingModel();
        var agent = AgentJobFactory.Create<string>("exhaust", model)
            .WithPrompt(s => s)
            .WithRetry(maxAttempts: 2, baseDelay: TimeSpan.FromMilliseconds(1))
            .MapResult((_, text) => text)
            .Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => agent.ExecuteAsync("go"));
        ex.Message.ShouldContain("LLM call failed after 2 attempts");
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

    private sealed class FailThenSucceedModel(int failCount, string successText) : IAgentModel
    {
        private int _calls;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            if (_calls++ < failCount)
                throw new InvalidOperationException("transient failure");
            return Task.FromResult(new AgentResponse { Text = successText });
        }
    }

    private sealed class AlwaysFailingModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("always fails");
    }
}
