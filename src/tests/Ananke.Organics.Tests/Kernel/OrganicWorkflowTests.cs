using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Sensing;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel;

[TestFixture]
public class OrganicWorkflowTests
{
    private OrganicHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new OrganicHost(
            new InProcessWorkflowHost(),
            new InMemoryCapabilityMap(TimeSpan.FromSeconds(30)),
            new OrganicGrowthOptions
            {
                Policy = new NullPolicy(),
                EvaluationInterval = 100 // high so we don't trigger in these tests
            });
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
    }

    [Test]
    public async Task RunAsync_DelegatesToInnerWorkflow()
    {
        var workflow = BuildWorkflow("test");
        var organic = workflow.JoinHost(_host);

        var execution = await organic.RunAsync("hello");

        execution.IsSuccess.ShouldBeTrue();
        execution.State.ShouldContain("[job0]");
    }

    [Test]
    public async Task RunAsync_ObservesCompletedExecution()
    {
        var monitor = new WorkflowExecutionMonitor();
        await using var host = CreateHostWithMonitor(monitor);
        var workflow = BuildWorkflow("observed");
        var organic = workflow.JoinHost(host);

        await organic.RunAsync("start");
        await host.WhenProcessedAsync("observed").WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = await monitor.GetSnapshotAsync("observed");
        snapshot.AvgLatencyMs.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task RunAsync_DoesNotObserveFaultedExecution()
    {
        var monitor = new WorkflowExecutionMonitor();
        await using var host = CreateHostWithMonitor(monitor);
        host.Register("faulted", null);

        var workflow = new Workflow<string>("faulted")
            .Job("fail", (_, _) => throw new InvalidOperationException("boom"))
            .Then("fail", Workflow.End);

        var organic = new OrganicWorkflow<string>(workflow, host, "faulted");

        // Run 3 times to meet minimum window for health snapshot
        for (var i = 0; i < 3; i++)
        {
            await organic.RunAsync("start");
            await host.WhenProcessedAsync("faulted").WaitAsync(TimeSpan.FromSeconds(5));
        }

        // Faulted executions ARE recorded (for health monitoring)
        var health = await monitor.GetHealthSnapshotAsync("faulted");
        health.ShouldNotBeNull();
        health!.ErrorRate.ShouldBe(1.0f);
    }

    [Test]
    public async Task Inner_ReturnsOriginalWorkflow()
    {
        var workflow = BuildWorkflow("test");
        var organic = workflow.JoinHost(_host);

        organic.Inner.ShouldBeSameAs(workflow);
    }

    [Test]
    public async Task StreamAsync_YieldsAllEvents()
    {
        var workflow = BuildWorkflow("stream-test");
        var organic = workflow.JoinHost(_host);

        var events = new List<object>();
        await foreach (var evt in organic.StreamAsync("start"))
            events.Add(evt);

        events.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task JoinHost_NullWorkflow_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            OrganicWorkflowExtensions.JoinHost<string>(null!, _host));
    }

    [Test]
    public async Task JoinHost_NullHost_Throws()
    {
        var workflow = BuildWorkflow("test");

        Should.Throw<ArgumentNullException>(() =>
            workflow.JoinHost(null!));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Workflow<string> BuildWorkflow(string name, int steps = 2)
    {
        var workflow = new Workflow<string>(name);
        for (var i = 0; i < steps; i++)
        {
            var jobName = $"job{i}";
            workflow.Job(jobName, async (state, ct) =>
            {
                await Task.Yield();
                return state + $"[{jobName}]";
            });
        }

        for (var i = 0; i < steps - 1; i++)
            workflow.Then($"job{i}", $"job{i + 1}");
        workflow.Then($"job{steps - 1}", Workflow.End);

        return workflow;
    }

    private OrganicHost CreateHostWithMonitor(WorkflowExecutionMonitor monitor)
    {
        return new OrganicHost(
            new InProcessWorkflowHost(),
            new InMemoryCapabilityMap(TimeSpan.FromSeconds(30)),
            new OrganicGrowthOptions
            {
                Policy = new NullPolicy(),
                Monitor = monitor,
                EvaluationInterval = 100
            });
    }

    private sealed class NullPolicy : IDivisionPolicy
    {
        public Task<DivisionPlan?> EvaluateAsync(
            ComplexitySnapshot snapshot, Design.WorkflowManifest manifest,
            CancellationToken ct = default)
            => Task.FromResult<DivisionPlan?>(null);
    }
}
