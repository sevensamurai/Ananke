using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Usage;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// ADR-arch-028 Part D: a ceiling that spans runs. Without persistence a crash-loop re-spends
/// the same budget indefinitely, which is the case a monthly limit exists for.
/// </summary>
[TestFixture]
public class PeriodBudgetTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ananke-period-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // 1M input tokens per call at $1/M => $1.00 per job.
    private static BudgetConfig Budget(decimal maxCost, decimal? periodLimit = null, decimal? warnPeriod = null) =>
        BudgetConfig.FromPerMillion(maxCost, inputPerMillion: 1m, outputPerMillion: 0m) with
        {
            PeriodCostLimit = periodLimit,
            WarnAtPeriodCost = warnPeriod
        };

    private Workflow<PeriodState> OneJob(BudgetConfig budget, IUsageRecorder? recorder)
    {
        var w = new Workflow<PeriodState>("period")
            .Job("work", AgentJob("work", new FixedUsageModel(1_000_000, 0)))
            .Then("work", Workflow.End)
            .WithBudget(budget);

        return recorder is null ? w : w.UseUsageRecorder(recorder);
    }

    private FileUsageRecorder Recorder(FakeTimeProvider clock) =>
        new(new BudgetId("acme-api"), _dir, clock);

    [Test]
    public async Task PeriodLimit_AccumulatesAcrossRuns_AndEventuallyStops()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        using var recorder = Recorder(clock);

        // Each run costs $1 and is well under its own MaxCost of $10.
        // The period ceiling is $2.50, so the third run must be refused.
        var statuses = new List<ExecutionStatus>();
        for (var i = 0; i < 4; i++)
        {
            var execution = await OneJob(Budget(maxCost: 10m, periodLimit: 2.5m), recorder)
                .RunAsync(new PeriodState());
            statuses.Add(execution.Status);
        }

        statuses[0].ShouldBe(ExecutionStatus.Completed);
        statuses[1].ShouldBe(ExecutionStatus.Completed);
        statuses[^1].ShouldBe(ExecutionStatus.BudgetExceeded,
            "spend accumulates across runs — each one alone stays under its own MaxCost");
    }

    [Test]
    public async Task PeriodLimit_SurvivesARestart()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));

        using (var first = Recorder(clock))
        {
            await OneJob(Budget(10m, periodLimit: 1.5m), first).RunAsync(new PeriodState());
            await OneJob(Budget(10m, periodLimit: 1.5m), first).RunAsync(new PeriodState());
        }

        // A brand-new recorder instance stands in for a restarted process.
        using var afterRestart = Recorder(clock);
        var execution = await OneJob(Budget(10m, periodLimit: 1.5m), afterRestart)
            .RunAsync(new PeriodState());

        execution.Status.ShouldBe(ExecutionStatus.BudgetExceeded,
            "a crash-loop must not get a fresh month's budget on every restart");
    }

    [Test]
    public async Task PeriodRollover_RestoresHeadroom_WithNoResetCall()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        using var recorder = Recorder(clock);

        await OneJob(Budget(10m, periodLimit: 1.5m), recorder).RunAsync(new PeriodState());
        var blocked = await OneJob(Budget(10m, periodLimit: 1.5m), recorder).RunAsync(new PeriodState());
        blocked.Status.ShouldBe(ExecutionStatus.BudgetExceeded);

        clock.SetUtcNow(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var newMonth = await OneJob(Budget(10m, periodLimit: 1.5m), recorder).RunAsync(new PeriodState());
        newMonth.Status.ShouldBe(ExecutionStatus.Completed,
            "the period is part of the key, so rollover needs no scheduled job");
    }

    [Test]
    public async Task RunLimit_StillBinds_Independently()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        using var recorder = Recorder(clock);

        // Generous period ceiling, tiny run ceiling: the run's own limit trips first.
        var execution = await OneJob(Budget(maxCost: 0.5m, periodLimit: 1000m), recorder)
            .RunAsync(new PeriodState());

        execution.Status.ShouldBe(ExecutionStatus.BudgetExceeded);
        // The message must name the ceiling that actually tripped.
        execution.Result!.Error!.ShouldNotContain("Period");
    }

    [Test]
    public async Task PeriodExceeded_SaysSoInTheError()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        using var recorder = Recorder(clock);

        await OneJob(Budget(10m, periodLimit: 0.5m), recorder).RunAsync(new PeriodState());
        var blocked = await OneJob(Budget(10m, periodLimit: 0.5m), recorder).RunAsync(new PeriodState());

        // An operator must be able to tell "this workflow was expensive" from "the month is spent".
        blocked.Result!.Error!.ShouldContain("Period");
    }

    [Test]
    public async Task PeriodWarning_FiresBeforeTheCeiling()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        using var recorder = Recorder(clock);

        await OneJob(Budget(10m, periodLimit: 100m, warnPeriod: 0.5m), recorder).RunAsync(new PeriodState());

        var events = new List<WorkflowEvent<PeriodState>>();
        await foreach (var e in OneJob(Budget(10m, periodLimit: 100m, warnPeriod: 0.5m), recorder)
                           .StreamAsync(new PeriodState()))
            events.Add(e);

        events.OfType<BudgetWarning<PeriodState>>().ShouldNotBeEmpty();
        events.OfType<BudgetExceeded<PeriodState>>().ShouldBeEmpty("a warning reports, it does not stop");
    }

    // -- Fail closed --------------------------------------------------

    [Test]
    public void PeriodLimit_WithoutARecorder_IsRefusedAtBuild()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            OneJob(Budget(10m, periodLimit: 50m), recorder: null).Build());

        // The message must name the call that fixes it.
        ex.Message.ShouldContain("UseUsageRecorder");
    }

    [Test]
    public void RunOnlyBudget_NeedsNoRecorder()
    {
        Should.NotThrow(() => OneJob(Budget(10m), recorder: null).Build(),
            "a per-execution ceiling is correctly served by the in-memory default");
    }

    [Test]
    public async Task UnreachableRecorder_FaultsTheRun_RatherThanRunningUnbudgeted()
    {
        var execution = await OneJob(Budget(10m, periodLimit: 50m), new ThrowingRecorder())
            .RunAsync(new PeriodState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted,
            "failing open would turn a storage outage into an unbounded spend window");
    }

    // -- Helpers -----------------------------------------------------

    private static Jobs.IJob<PeriodState> AgentJob(string name, IAgentModel model) =>
        AgentJobFactory.Create<PeriodState, AgentOutput>(name, model)
            .WithPrompt(_ => "test")
            .MapResult((s, _) => s)
            .Build();

    public record PeriodState;

    private record AgentOutput;

    private sealed class ThrowingRecorder : IUsageRecorder
    {
        public Task RecordUsageAsync(UsageRecord record, CancellationToken ct = default) =>
            throw new IOException("budget store unreachable");

        public Task<UsageSnapshot> ReadAsync(CancellationToken ct = default) =>
            throw new IOException("budget store unreachable");

        public Task ResetAsync(CancellationToken ct = default) =>
            throw new IOException("budget store unreachable");
    }

    private sealed class FixedUsageModel(int inputTokens, int outputTokens) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                Text = "{}",
                Usage = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens }
            });
    }
}
