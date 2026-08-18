using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Usage;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A workflow can mix jobs whose model reports a per-call cost (a profile-aware router) with
/// jobs on a plain IAgentModel that report tokens only. The budget must account for both.
/// </summary>
[TestFixture]
public class MixedCostSourceTests
{
    private static TokenUsage Tokens(int input, int output = 0) =>
        new() { InputTokens = input, OutputTokens = output };

    // $1 per million input tokens, so 1M uncosted tokens are worth exactly 1.00.
    private static BudgetConfig FlatRates(decimal maxCost = 1000m) =>
        BudgetConfig.FromPerMillion(maxCost, inputPerMillion: 1m, outputPerMillion: 0m);

    [Test]
    public async Task AllModelCosted_UsesTheReportedCost()
    {
        var recorder = new InMemoryUsageRecorder();
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000), 0.15m));
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000), 0.25m));

        var verdict = await new BudgetGate(recorder, UsageSnapshot.Empty, FlatRates())
            .EvaluateAsync(CancellationToken.None);

        verdict.RunCost.ShouldBe(0.40m, "reported per-call cost wins over flat rates");
    }

    [Test]
    public async Task NoneModelCosted_FallsBackToFlatRates()
    {
        var recorder = new InMemoryUsageRecorder();
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000)));
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(500_000)));

        var verdict = await new BudgetGate(recorder, UsageSnapshot.Empty, FlatRates())
            .EvaluateAsync(CancellationToken.None);

        verdict.RunCost.ShouldBe(1.50m);
    }

    /// <summary>
    /// The defect. One routed job sets HasModelBasedCost, after which the whole total was taken
    /// from AccumulatedCost — which only ever received the routed job's cost. Every plain-model
    /// job in the same workflow spent invisibly, and Build()'s rate check passes because it asks
    /// whether *any* job is profile-aware.
    /// </summary>
    [Test]
    public async Task MixedSources_CountBothPortions()
    {
        var recorder = new InMemoryUsageRecorder();
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000), 0.15m));  // routed
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000)));          // plain

        var verdict = await new BudgetGate(recorder, UsageSnapshot.Empty, FlatRates())
            .EvaluateAsync(CancellationToken.None);

        verdict.RunCost.ShouldBe(1.15m,
            "0.15 reported for the routed job, plus 1.00 of flat-rated tokens from the plain one");
    }

    [Test]
    public async Task MixedSources_TripTheLimitTheyShould()
    {
        var recorder = new InMemoryUsageRecorder();
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000), 0.15m));
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000)));

        // Ceiling of 1.00: reachable only if the uncosted portion is counted.
        var verdict = await new BudgetGate(recorder, UsageSnapshot.Empty, FlatRates(maxCost: 1.0m))
            .EvaluateAsync(CancellationToken.None);

        verdict.State.ShouldBe(BudgetState.Exceeded,
            "under-counting means a budget that never fires — the failure this guards against");
    }

    [Test]
    public async Task Baseline_SubtractsBothPortions()
    {
        var recorder = new InMemoryUsageRecorder();
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000), 0.15m));
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(1_000_000)));
        var baseline = await recorder.ReadAsync();

        // A second run against the same recorder — as a sub-workflow would be.
        await recorder.RecordUsageAsync(new UsageRecord(Tokens(2_000_000)));

        var verdict = await new BudgetGate(recorder, baseline, FlatRates())
            .EvaluateAsync(CancellationToken.None);

        verdict.RunCost.ShouldBe(2.00m, "only what this run added, in both portions");
        verdict.PeriodCost.ShouldBe(0m, "no period limit configured");
    }
}
