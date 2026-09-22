using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// How work below a job says what it is doing.
/// </summary>
/// <remarks>
/// <para>
/// A job cannot reach the runner's channel, and the runner must not learn what any particular job
/// does — a runner that special-cased one job type would not have extended the event seam, it would
/// have bypassed it. So the test that matters here uses an event type <b>declared in this test
/// assembly</b>: if it reaches a reader, nothing in the runner could have known about it.
/// </para>
/// <para>
/// The same route carries a sub-workflow's events to its parent's stream, which had been dropped
/// entirely: a consumer saw one <c>JobCompleted</c> for the subflow and nothing of what happened
/// inside it.
/// </para>
/// </remarks>
[TestFixture]
public class WorkflowEventReportingTests
{
    /// <summary>An event the runner has never heard of, carrying no workflow state.</summary>
    private sealed record NodeRuled : WorkflowEvent
    {
        public required string NodeId { get; init; }
    }

    // ── A job reporting ──

    [Test]
    public async Task AJobThatReports_ReachesTheStream()
    {
        var events = await CollectAsync(Reporting("work", ["first", "second"]));

        events.OfType<NodeRuled>().Select(e => e.NodeId).ShouldBe(["first", "second"]);
    }

    [Test]
    public async Task AJobThatReports_ArrivesBetweenItsOwnStartedAndCompleted()
    {
        var events = await CollectAsync(Reporting("work", ["first"]));

        var started = events.FindIndex(e => e is JobStarted<CounterState> { JobName: "work" });
        var reported = events.FindIndex(e => e is NodeRuled);
        var completed = events.FindIndex(e => e is JobCompleted<CounterState> { JobName: "work" });

        reported.ShouldBeGreaterThan(started);
        reported.ShouldBeLessThan(completed);
    }

    [Test]
    public async Task AJobThatReports_UnderRunAsync_IsANoOpRatherThanAFailure()
    {
        // The same job has to work whether or not anyone is streaming it, or a supervised job
        // would only be runnable one of the two ways.
        var result = await Reporting("work", ["first"]).RunAsync(new CounterState());

        result.Status.ShouldBe(ExecutionStatus.Completed);
    }

    [Test]
    public async Task ReportAsync_WithNothingScoped_IsANoOp() =>
        await WorkflowEventReporting.ReportAsync(
            new NodeRuled { WorkflowName = "none", ExecutionId = "none", NodeId = "first" });

    // ── The scope ──

    [Test]
    public void BeginScope_DoesNotNest_TheOutermostSinkWins()
    {
        var outer = new Collecting();
        var inner = new Collecting();

        using var outerScope = WorkflowEventReporting.BeginScope(outer);
        using (var innerScope = WorkflowEventReporting.BeginScope(inner))
        {
            innerScope.IsOwner.ShouldBeFalse();
            WorkflowEventReporting.Current.ShouldBeSameAs(outer);
        }

        outerScope.IsOwner.ShouldBeTrue();
        WorkflowEventReporting.Current.ShouldBeSameAs(outer);
    }

    [Test]
    public void BeginScope_OnDispose_RestoresWhatWasThereBefore()
    {
        using (WorkflowEventReporting.BeginScope(new Collecting()))
            WorkflowEventReporting.Current.ShouldNotBeNull();

        WorkflowEventReporting.Current.ShouldBeNull();
    }

    // ── A sub-workflow ──

    [Test]
    public async Task ASubFlow_ItsInnerJobEvents_ReachTheOuterStream()
    {
        var child = new Workflow<CounterState>("child")
            .Job("inner-a", (s, _) => Task.FromResult(s))
            .Job("inner-b", (s, _) => Task.FromResult(s))
            .Chain("inner-a", "inner-b")
            .Then("inner-b", Workflow.End);

        var parent = new Workflow<CounterState>("parent")
            .Job("before", (s, _) => Task.FromResult(s))
            .SubFlow("nested", child, s => s, (s, _) => s)
            .Chain("before", "nested")
            .Then("nested", Workflow.End);

        var events = await CollectAsync(parent);

        var jobs = events.OfType<JobStarted<CounterState>>().Select(e => e.JobName).ToList();

        // Without this, a consumer saw "nested" start and finish with nothing in between.
        jobs.ShouldContain("inner-a");
        jobs.ShouldContain("inner-b");
    }

    [Test]
    public async Task ASubFlows_InnerEvents_NameTheInnerWorkflow()
    {
        var child = new Workflow<CounterState>("child")
            .Job("inner-a", (s, _) => Task.FromResult(s))
            .Then("inner-a", Workflow.End);

        var parent = new Workflow<CounterState>("parent")
            .SubFlow("nested", child, s => s, (s, _) => s)
            .Then("nested", Workflow.End);

        var events = await CollectAsync(parent);

        // They are the child's events, and they say so — otherwise a reader could not tell an
        // inner job from an outer one with the same name.
        var inner = events.OfType<JobStarted<CounterState>>().First(e => e.JobName == "inner-a");
        inner.WorkflowName.ShouldBe("child");
        inner.ExecutionId.ShouldNotBe(events[0].ExecutionId);
    }

    // ── Fixtures ──

    private static Workflow<CounterState> Reporting(string jobName, IReadOnlyList<string> nodeIds) =>
        new Workflow<CounterState>("reporting")
            .Job(jobName, new ReportingJob(nodeIds))
            .Then(jobName, Workflow.End);

    private static async Task<List<WorkflowEvent>> CollectAsync(Workflow<CounterState> workflow)
    {
        var events = new List<WorkflowEvent>();
        await foreach (var evt in workflow.StreamAsync(new CounterState()))
            events.Add(evt);
        return events;
    }

    /// <summary>A job that reports progress of its own, knowing nothing about channels.</summary>
    private sealed class ReportingJob(IReadOnlyList<string> nodeIds) : IJob<CounterState>
    {
        public string Name => "reporting-job";

        public async Task<CounterState> ExecuteAsync(CounterState state, CancellationToken ct = default)
        {
            foreach (var nodeId in nodeIds)
            {
                await WorkflowEventReporting.ReportAsync(
                    new NodeRuled { WorkflowName = "reporting", ExecutionId = "n/a", NodeId = nodeId },
                    ct);
            }

            return state;
        }
    }

    private sealed class Collecting : IWorkflowEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
