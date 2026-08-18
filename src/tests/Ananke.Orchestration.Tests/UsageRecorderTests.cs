using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Usage;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class InMemoryUsageRecorderTests
{
    private static UsageRecord Tokens(int input, int output, decimal? cost = null) =>
        new(new TokenUsage { InputTokens = input, OutputTokens = output }, cost);

    [Test]
    public async Task RecordUsage_Accumulates()
    {
        var recorder = new InMemoryUsageRecorder();

        await recorder.RecordUsageAsync(Tokens(10, 5));
        await recorder.RecordUsageAsync(Tokens(1, 2));

        var snapshot = await recorder.ReadAsync();
        snapshot.Usage.InputTokens.ShouldBe(11);
        snapshot.Usage.OutputTokens.ShouldBe(7);
        snapshot.Usage.TotalTokens.ShouldBe(18);
    }

    [Test]
    public async Task RecordUsage_WithoutModelCost_LeavesCostUnflagged()
    {
        var recorder = new InMemoryUsageRecorder();

        await recorder.RecordUsageAsync(Tokens(10, 5));

        var snapshot = await recorder.ReadAsync();
        snapshot.HasModelBasedCost.ShouldBeFalse(
            "no per-call rate was reported, so a budget must fall back to flat rates");
        snapshot.AccumulatedCost.ShouldBe(0m);
    }

    [Test]
    public async Task RecordUsage_WithModelCost_AccumulatesAndFlags()
    {
        var recorder = new InMemoryUsageRecorder();

        await recorder.RecordUsageAsync(Tokens(10, 5, cost: 0.25m));
        await recorder.RecordUsageAsync(Tokens(1, 1, cost: 0.75m));

        var snapshot = await recorder.ReadAsync();
        snapshot.HasModelBasedCost.ShouldBeTrue();
        snapshot.AccumulatedCost.ShouldBe(1.00m);
    }

    [Test]
    public async Task Snapshot_DoesNotChangeWhenRecorderDoes()
    {
        var recorder = new InMemoryUsageRecorder();
        await recorder.RecordUsageAsync(Tokens(10, 0));

        var before = await recorder.ReadAsync();
        await recorder.RecordUsageAsync(Tokens(90, 0));

        before.Usage.InputTokens.ShouldBe(10,
            "a snapshot is an immutable value, not a live view into recorder state");
        (await recorder.ReadAsync()).Usage.InputTokens.ShouldBe(100);
    }

    [Test]
    public async Task Reset_ClearsEverything()
    {
        var recorder = new InMemoryUsageRecorder();
        await recorder.RecordUsageAsync(Tokens(10, 5, cost: 2m));

        await recorder.ResetAsync();

        var snapshot = await recorder.ReadAsync();
        snapshot.Usage.TotalTokens.ShouldBe(0);
        snapshot.AccumulatedCost.ShouldBe(0m);
        snapshot.HasModelBasedCost.ShouldBeFalse();
    }

    /// <summary>
    /// The property the whole design rests on (ADR-arch-028 D9): fork branches record
    /// concurrently, so a read-modify-write without a lock loses updates. The old
    /// UsageAccumulator.Add was exactly that, safe only because each job owned a private
    /// instance and main-path jobs are sequential.
    /// </summary>
    [Test]
    public async Task RecordUsage_UnderConcurrency_LosesNoUpdates()
    {
        var recorder = new InMemoryUsageRecorder();
        var writers = Math.Max(8, Environment.ProcessorCount * 2);
        const int perWriter = 5_000;

        // All writers released at once: staggered starts let the read-modify-write
        // windows miss each other, which is what made an earlier version of this test
        // pass even with the lock removed.
        using var gate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, writers).Select(_ => Task.Run(async () =>
        {
            gate.Wait();
            for (var i = 0; i < perWriter; i++)
                await recorder.RecordUsageAsync(Tokens(1, 1, cost: 0.01m));
        })).ToArray();

        gate.Set();
        await Task.WhenAll(tasks);

        var expected = writers * perWriter;
        var snapshot = await recorder.ReadAsync();
        snapshot.Usage.InputTokens.ShouldBe(expected);
        snapshot.Usage.OutputTokens.ShouldBe(expected);
        snapshot.AccumulatedCost.ShouldBe(expected * 0.01m);
    }
}

[TestFixture]
public class UsageRecordingScopeTests
{
    [Test]
    public void Current_WithNoScope_IsNull()
    {
        UsageRecording.Current.ShouldBeNull();
    }

    [Test]
    public void BeginScope_MakesRecorderCurrent_AndRestoresOnDispose()
    {
        var recorder = new InMemoryUsageRecorder();

        using (var scope = UsageRecording.BeginScope(recorder))
        {
            scope.IsOwner.ShouldBeTrue();
            UsageRecording.Current.ShouldBeSameAs(recorder);
        }

        UsageRecording.Current.ShouldBeNull();
    }

    /// <summary>
    /// A nested runner — SubFlowJob builds its own WorkflowRunner — must not shadow the
    /// outer recorder. Shadowing is precisely how sub-workflow spend became invisible to
    /// the parent's budget.
    /// </summary>
    [Test]
    public void BeginScope_WhenAlreadyScoped_KeepsTheOuterRecorder()
    {
        var outer = new InMemoryUsageRecorder();
        var inner = new InMemoryUsageRecorder();

        using (UsageRecording.BeginScope(outer))
        {
            using (var nested = UsageRecording.BeginScope(inner))
            {
                nested.IsOwner.ShouldBeFalse();
                UsageRecording.Current.ShouldBeSameAs(outer,
                    "a nested scope must inherit the outermost recorder, not replace it");
            }

            UsageRecording.Current.ShouldBeSameAs(outer,
                "disposing a non-owning nested scope must not clear the outer recorder");
        }
    }

    [Test]
    public async Task Scope_FlowsIntoConcurrentBranches()
    {
        var recorder = new InMemoryUsageRecorder();

        using (UsageRecording.BeginScope(recorder))
        {
            // Mirrors a fork: N tasks started inside the scope, each recording into it.
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                UsageRecording.Current.ShouldBeSameAs(recorder);
                await UsageRecording.Current!.RecordUsageAsync(
                    new UsageRecord(new TokenUsage { InputTokens = 5 }));
            })));
        }

        (await recorder.ReadAsync()).Usage.InputTokens.ShouldBe(20);
    }
}
