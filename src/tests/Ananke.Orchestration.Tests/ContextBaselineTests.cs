using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The measurement pass: turning what a run did into the numbers a later claim can be judged
/// against.
/// </summary>
/// <remarks>
/// <para>
/// These figures are not targets. Several of them improve by suppression — a workflow that attempts
/// less truncates less and repeats less — so each is only meaningful against an outcome held
/// constant. What they are for is making a change <em>attributable</em>: without a before, an after
/// is an anecdote.
/// </para>
/// <para>
/// The end-to-end tests below run an actual agent job through the real engine, because the thing
/// most likely to be wrong is not the arithmetic — it is a seam that never fires.
/// </para>
/// </remarks>
[TestFixture]
public class ContextBaselineTests
{
    // ── Duplicate tool-call detection ──

    [Test]
    public async Task Trajectory_TheSameCallTwice_IsCountedAsRework()
    {
        var observer = new CapturingTrajectoryObserver();
        var model = ToolSequence(
            Call("c1", "read", """{"path":"a.txt"}"""),
            Call("c2", "read", """{"path":"a.txt"}"""));

        await Job("repeats", model, observer).ExecuteAsync(new S { In = "go" });

        var snapshot = observer.Snapshots.ShouldHaveSingleItem();
        snapshot.TotalToolCalls.ShouldBe(2);
        snapshot.DistinctToolCalls.ShouldBe(1);
        snapshot.DuplicateToolCalls.ShouldBe(1);
        snapshot.ReworkRate.ShouldBe(0.5f);
    }

    [Test]
    public async Task Trajectory_DifferentArguments_AreNotRework()
    {
        var observer = new CapturingTrajectoryObserver();
        var model = ToolSequence(
            Call("c1", "read", """{"path":"a.txt"}"""),
            Call("c2", "read", """{"path":"b.txt"}"""));

        await Job("distinct", model, observer).ExecuteAsync(new S { In = "go" });

        observer.Snapshots[0].DuplicateToolCalls.ShouldBe(0);
        observer.Snapshots[0].DistinctToolCalls.ShouldBe(2);
    }

    [Test]
    public async Task Trajectory_ArgumentsDifferingOnlyInFormatting_AreStillRework()
    {
        // A model re-emitting the same call with different whitespace has still repeated itself,
        // and a raw string comparison would have missed it.
        var observer = new CapturingTrajectoryObserver();
        var model = ToolSequence(
            Call("c1", "read", """{"path":"a.txt"}"""),
            Call("c2", "read", """{  "path" :  "a.txt"  }"""));

        await Job("formatting", model, observer).ExecuteAsync(new S { In = "go" });

        observer.Snapshots[0].DuplicateToolCalls.ShouldBe(1);
    }

    [Test]
    public async Task Trajectory_NoToolCalls_ReportsZeroReworkRatherThanDividingByZero()
    {
        var observer = new CapturingTrajectoryObserver();

        await Job("quiet", SequencedModel.Of(Answer("done")), observer)
            .ExecuteAsync(new S { In = "go" });

        observer.Snapshots[0].ReworkRate.ShouldBe(0f);
        observer.Snapshots[0].DuplicateToolCalls.ShouldBe(0);
    }

    // ── The collector's arithmetic ──

    [Test]
    public void Baseline_FillPercentiles_UseNearestRankSoTheyNameCallsThatHappened()
    {
        // An interpolated p99 over a handful of samples invents a value that never occurred, and
        // the tail is the whole point of looking.
        var collector = new ContextBaselineCollector();
        foreach (var fill in new[] { 0.10, 0.20, 0.30, 0.40, 0.99 })
            Observe(collector, fill);

        var baseline = collector.Snapshot();

        baseline.FillP95.ShouldBe(0.99);
        baseline.FillP99.ShouldBe(0.99);
    }

    [Test]
    public void Baseline_TruncationIsComputedOverWindowKnownCallsOnly_AndCoverageIsReported()
    {
        var collector = new ContextBaselineCollector();
        Observe(collector, fill: 1.5);                   // over its window
        Observe(collector, fill: 0.5);
        ObserveWithoutWindow(collector);
        ObserveWithoutWindow(collector);

        var baseline = collector.Snapshot();

        baseline.Calls.ShouldBe(4);
        baseline.CallsWithKnownWindow.ShouldBe(2);
        baseline.WindowCoverage.ShouldBe(0.5);
        baseline.TruncationIncidence.ShouldBe(0.5);      // 1 of the 2 that could be judged
    }

    [Test]
    public void Baseline_WithNoWindowEverKnown_SaysSoRatherThanReportingZero()
    {
        // Zero truncation over zero measurable calls is not a clean bill of health, and a report
        // that prints "0.0%" invites reading it as one.
        var collector = new ContextBaselineCollector();
        ObserveWithoutWindow(collector);

        var report = collector.Snapshot().ToReport();

        report.ShouldContain("n/a — no call knew its window");
        report.ShouldNotContain("target zero");
    }

    [Test]
    public void Baseline_PartialCoverage_IsCalledOutInTheReport()
    {
        var collector = new ContextBaselineCollector();
        Observe(collector, fill: 0.5);
        ObserveWithoutWindow(collector);

        collector.Snapshot().ToReport().ShouldContain("1 call(s) had no known window");
    }

    [Test]
    public void Baseline_AnEmptyRun_ProducesAReportRatherThanThrowing()
    {
        var baseline = new ContextBaselineCollector().Snapshot();

        baseline.Calls.ShouldBe(0);
        baseline.WindowCoverage.ShouldBe(0);
        Should.NotThrow(() => baseline.ToReport());
    }

    // ── End to end, through the real engine ──

    [Test]
    public async Task EndToEnd_ARunUnderARoutedModel_ProducesEveryFigureInTheBaseline()
    {
        var collector = new ContextBaselineCollector();
        using var scope = ContextObserving.BeginScope(collector);

        var model = ToolSequence(
            Call("c1", "read", """{"path":"a.txt"}"""),
            Call("c2", "read", """{"path":"a.txt"}"""),      // the rework
            Call("c3", "read", """{"path":"b.txt"}"""));

        var job = AgentJobFactory.Create<S>("baseline", new WindowedModel(model, window: 4_000))
            .WithPrompt(s => s.In)
            .WithTools(ReadKit())
            .WithContract(new AgentContract
            {
                Goal = "Read the files",
                Constraints = ["Paths stay relative to the repository root."]
            })
            .WithContextStrategy(new SlidingWindowContextStrategy(maxTokens: 400))
            .WithTrajectoryObserver(collector)
            .MapResult((s, text) => s with { Out = text })
            .Build();

        await job.ExecuteAsync(new S { In = "go" });

        var baseline = collector.Snapshot();

        // Every seam fired: assemblies, compactions, and the trajectory.
        baseline.Calls.ShouldBeGreaterThan(0);
        baseline.CallsWithKnownWindow.ShouldBe(baseline.Calls);
        baseline.WindowCoverage.ShouldBe(1);
        baseline.Compactions.ShouldBeGreaterThan(0);
        baseline.Episodes.ShouldBe(1);

        // The rework the model was scripted to do is the rework that gets reported.
        baseline.ToolCalls.ShouldBe(3);
        baseline.ReworkRate.ShouldBe(1 / 3d, tolerance: 0.001);

        // The pinned contract is charged to every assembly, and shows up as a floor.
        baseline.MeanContractTokens.ShouldBeGreaterThan(0);

        baseline.ToReport().ShouldContain("rework rate");
    }

    [Test]
    public async Task EndToEnd_AModelWhoseWindowIsUnknown_ReportsZeroCoverageRatherThanGuessing()
    {
        // The failure this guards is a baseline that looks healthy because nothing could be judged.
        var collector = new ContextBaselineCollector();
        using var scope = ContextObserving.BeginScope(collector);

        var job = AgentJobFactory.Create<S>("unrouted", SequencedModel.Of(Answer("done")))
            .WithPrompt(s => s.In)
            .WithContextStrategy(new SlidingWindowContextStrategy(maxTokens: 400))
            .MapResult((s, text) => s with { Out = text })
            .Build();

        await job.ExecuteAsync(new S { In = "go" });

        var baseline = collector.Snapshot();
        baseline.WindowCoverage.ShouldBe(0);
        baseline.ToReport().ShouldContain("n/a");
    }

    // ── Tool schemas are spoken for before a strategy chooses anything ──

    [Test]
    public void Budget_SubtractsWhatIsAlreadyCommitted()
    {
        var budget = new ContextBudget { ModelContextTokens = 1_000, ReservedTokens = 250 };

        budget.Resolve(1_000).ShouldBe(750);
    }

    [Test]
    public void Budget_ReservationLargerThanTheWindow_ClampsAtZeroRatherThanGoingNegative()
    {
        // Schemas bigger than the whole window are a routing problem. Returning a negative budget
        // would turn it into an arithmetic one somewhere further away.
        new ContextBudget { ModelContextTokens = 100, ReservedTokens = 400 }.Resolve(100).ShouldBe(0);
    }

    [Test]
    public async Task EndToEnd_AStrategysBudget_LeavesRoomForTheToolSchemas()
    {
        // Tool definitions are sent on every call and are often the largest single contributor, but
        // ApplyAsync only ever sees messages and the system prompt. Without the reservation a
        // strategy aimed at exactly the window overflows it by the size of the schemas — while
        // reporting that it compacted successfully.
        var strategy = new BudgetRecordingStrategy();
        var kit = ReadKit();
        var schemaCost = RequestTokenEstimator.Estimate(new AgentRequest
        {
            Messages = [],
            Tools = [.. kit.Tools.Values.Select(t => new AgentTool(t.Name, t.Description, t.ParametersJsonSchema))]
        });

        var job = AgentJobFactory.Create<S>("reserved", new WindowedModel(SequencedModel.Of(Answer("done")), 1_000))
            .WithPrompt(s => s.In)
            .WithTools(kit)
            .WithContextStrategy(strategy)
            .MapResult((s, text) => s with { Out = text })
            .Build();

        await job.ExecuteAsync(new S { In = "go" });

        schemaCost.ShouldBeGreaterThan(0);
        strategy.Applied.ShouldContain(1_000 - schemaCost);
    }

    // ── Fixtures ──

    private static void Observe(ContextBaselineCollector collector, double fill) =>
        collector.OnContextAssembledAsync(new ContextObservation
        {
            PromptTokens = (int)(fill * 1000),
            ContextTokens = 1000
        }).AsTask().Wait();

    private static void ObserveWithoutWindow(ContextBaselineCollector collector) =>
        collector.OnContextAssembledAsync(new ContextObservation { PromptTokens = 100 })
            .AsTask().Wait();

    private static TextAgentJob<S> Job(string name, IAgentModel model, ITrajectoryObserver observer) =>
        AgentJobFactory.Create<S>(name, model)
            .WithPrompt(s => s.In)
            .WithTools(ReadKit())
            .WithTrajectoryObserver(observer)
            .MapResult((s, text) => s with { Out = text })
            .Build();

    private static ToolKit ReadKit() =>
        new ToolKit("io").AddTool(
            "read", "Reads a file",
            configure: b => b.Param("path", "The file to read")
                .OnExecute(args => Task.FromResult(ToolResult.Ok($"contents of {args.Get("path")}"))));

    private static AgentResponse Call(string id, string tool, string args) =>
        new() { Text = "working", ToolCalls = [new AgentToolCall(id, tool, args)] };

    private static AgentResponse Answer(string text) => new() { Text = text };

    private static SequencedModel ToolSequence(params AgentResponse[] calls) =>
        SequencedModel.Of([.. calls, Answer("done")]);

    private sealed record S
    {
        public string In { get; init; } = string.Empty;
        public string Out { get; init; } = string.Empty;
    }

    private sealed class SequencedModel : IAgentModel
    {
        private readonly AgentResponse[] _responses;
        private int _index;

        private SequencedModel(AgentResponse[] responses) => _responses = responses;

        public static SequencedModel Of(params AgentResponse[] responses) => new(responses);

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(_responses[Math.Min(_index++, _responses.Length - 1)]);
    }

    /// <summary>A model that knows its own window, the way a routed one does.</summary>
    private sealed class WindowedModel(IAgentModel inner, int window)
        : IAgentModel, IModelContextResolver
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            inner.GenerateAsync(request, ct);

        public ModelContextWindow ResolveContextWindow(AgentRequest request) =>
            new("stub-model", window);
    }

    /// <summary>Records the budget each assembly resolved to.</summary>
    private sealed class BudgetRecordingStrategy : IContextStrategy
    {
        public List<int> Applied { get; } = [];

        public Task<ContextProjection> ApplyAsync(
            IReadOnlyList<AgentMessage> messages,
            string? systemPrompt,
            ContextBudget budget,
            CancellationToken ct = default)
        {
            var applied = budget.Resolve(int.MaxValue);
            Applied.Add(applied);
            return Task.FromResult(ContextProjection.Unchanged(messages, applied));
        }
    }

    private sealed class CapturingTrajectoryObserver : ITrajectoryObserver
    {
        public List<TrajectorySnapshot> Snapshots { get; } = [];

        public ValueTask OnTrajectoryCompleteAsync(
            TrajectorySnapshot snapshot, CancellationToken ct = default)
        {
            Snapshots.Add(snapshot);
            return ValueTask.CompletedTask;
        }
    }
}
