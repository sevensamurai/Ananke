using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Usage;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// ADR-arch-028 D15: the shipped default for period budgets. The point is survival across
/// process restarts — without persistence a crash-loop re-spends the same budget indefinitely.
/// </summary>
[TestFixture]
public class FileUsageRecorderTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ananke-budget-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static UsageRecord Tokens(int input, int output, decimal? cost = null) =>
        new(new TokenUsage { InputTokens = input, OutputTokens = output }, cost);

    private FileUsageRecorder Recorder(FakeTimeProvider clock, string id = "acme-api", int anchor = 1) =>
        new(new BudgetId(id), _dir, clock, anchor);

    private static FakeTimeProvider ClockAt(int y, int m, int d) =>
        new(new DateTimeOffset(y, m, d, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task Records_Accumulate()
    {
        var clock = ClockAt(2026, 8, 10);
        using var recorder = Recorder(clock);

        await recorder.RecordUsageAsync(Tokens(100, 20, cost: 0.5m));
        await recorder.RecordUsageAsync(Tokens(50, 10, cost: 0.25m));

        var snapshot = await recorder.ReadAsync();
        snapshot.Usage.InputTokens.ShouldBe(150);
        snapshot.Usage.OutputTokens.ShouldBe(30);
        snapshot.AccumulatedCost.ShouldBe(0.75m);
        snapshot.HasModelBasedCost.ShouldBeTrue();
    }

    /// <summary>The whole reason this class exists.</summary>
    [Test]
    public async Task Totals_SurviveANewRecorderInstance()
    {
        var clock = ClockAt(2026, 8, 10);

        using (var first = Recorder(clock))
            await first.RecordUsageAsync(Tokens(100, 0, cost: 1m));

        // A new instance stands in for a restarted process.
        using var second = Recorder(clock);
        var snapshot = await second.ReadAsync();

        snapshot.Usage.InputTokens.ShouldBe(100);
        snapshot.AccumulatedCost.ShouldBe(1m,
            "a crash-loop must not get a fresh budget each time it restarts");
    }

    [Test]
    public async Task NewPeriod_StartsAtZero_WithNoResetCall()
    {
        var clock = ClockAt(2026, 8, 20);
        using var recorder = Recorder(clock);
        await recorder.RecordUsageAsync(Tokens(100, 0, cost: 5m));

        (await recorder.ReadAsync()).AccumulatedCost.ShouldBe(5m);

        // Roll into September. Nothing is scheduled; the key simply changes.
        clock.SetUtcNow(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var fresh = await recorder.ReadAsync();
        fresh.AccumulatedCost.ShouldBe(0m);
        fresh.Usage.TotalTokens.ShouldBe(0);
    }

    [Test]
    public async Task PreviousPeriod_IsStillOnDisk_AfterRollover()
    {
        var clock = ClockAt(2026, 8, 20);
        using var recorder = Recorder(clock);
        await recorder.RecordUsageAsync(Tokens(100, 0, cost: 5m));
        var augustFile = recorder.CurrentPath();

        clock.SetUtcNow(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        recorder.CurrentPath().ShouldNotBe(augustFile);
        File.Exists(augustFile).ShouldBeTrue("rollover changes the key; it does not delete history");
    }

    [Test]
    public async Task DistinctBudgetIds_DoNotShareATotal()
    {
        var clock = ClockAt(2026, 8, 10);
        using var a = Recorder(clock, "team-a");
        using var b = Recorder(clock, "team-b");

        await a.RecordUsageAsync(Tokens(100, 0, cost: 1m));

        (await b.ReadAsync()).AccumulatedCost.ShouldBe(0m);
        (await a.ReadAsync()).AccumulatedCost.ShouldBe(1m);
    }

    /// <summary>
    /// A BudgetId is a storage key and may contain separators; the file name is derived from a
    /// hash rather than the key, so a key like "../escape" cannot write outside the directory.
    /// </summary>
    [Test]
    public async Task KeysContainingPathCharacters_StayInsideTheDirectory()
    {
        var clock = ClockAt(2026, 8, 10);
        using var recorder = Recorder(clock, "../../escape:me/now");

        await recorder.RecordUsageAsync(Tokens(10, 0, cost: 1m));

        var written = Path.GetFullPath(recorder.CurrentPath());
        written.ShouldStartWith(Path.GetFullPath(_dir));
        Directory.GetFiles(_dir).ShouldHaveSingleItem();
        (await recorder.ReadAsync()).AccumulatedCost.ShouldBe(1m);
    }

    [Test]
    public async Task DifferentKeysThatSanitiseAlike_StillDoNotCollide()
    {
        var clock = ClockAt(2026, 8, 10);
        using var a = Recorder(clock, "acme/api");
        using var b = Recorder(clock, "acme:api");

        await a.RecordUsageAsync(Tokens(10, 0, cost: 1m));

        (await b.ReadAsync()).AccumulatedCost.ShouldBe(0m,
            "the hash disambiguates keys that a naive sanitiser would flatten together");
    }

    [Test]
    public async Task Reset_ClearsOnlyTheCurrentPeriod()
    {
        var clock = ClockAt(2026, 8, 10);
        using var recorder = Recorder(clock);
        await recorder.RecordUsageAsync(Tokens(100, 0, cost: 5m));

        await recorder.ResetAsync();

        (await recorder.ReadAsync()).AccumulatedCost.ShouldBe(0m);
    }

    [Test]
    public async Task ConcurrentRecords_LoseNoUpdates()
    {
        var clock = ClockAt(2026, 8, 10);
        using var recorder = Recorder(clock);

        const int writers = 8, perWriter = 25;
        using var gate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, writers).Select(_ => Task.Run(async () =>
        {
            gate.Wait();
            for (var i = 0; i < perWriter; i++)
                await recorder.RecordUsageAsync(Tokens(1, 1, cost: 0.01m));
        })).ToArray();

        gate.Set();
        await Task.WhenAll(tasks);

        var snapshot = await recorder.ReadAsync();
        snapshot.Usage.InputTokens.ShouldBe(writers * perWriter);
        snapshot.AccumulatedCost.ShouldBe(writers * perWriter * 0.01m);
    }

    [Test]
    public async Task AnchorDay_IsHonoured()
    {
        // Anchor 15: on 10 August the live period is the one that began 15 July.
        var clock = ClockAt(2026, 8, 10);
        using var recorder = Recorder(clock, anchor: 15);

        recorder.CurrentPath().ShouldEndWith("_2026-07-15.json");

        await recorder.RecordUsageAsync(Tokens(10, 0, cost: 1m));
        clock.SetUtcNow(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));

        (await recorder.ReadAsync()).AccumulatedCost.ShouldBe(0m, "15 August starts a new period");
    }
}
