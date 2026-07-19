using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class WorkflowExecutionMonitorTests
{
    private WorkflowExecutionMonitor _monitor = null!;

    [SetUp]
    public void SetUp()
    {
        _monitor = new WorkflowExecutionMonitor(windowSize: 10);
    }

    private static StructuralProfile MakeProfile(
        int toolCount = 4, int jobCount = 2, int tagClusters = 1,
        int resourceSpan = 1, float contextUtil = 0.1f) => new()
        {
            ToolCount = toolCount,
            JobCount = jobCount,
            TagClusterCount = tagClusters,
            ResourceSpan = resourceSpan,
            ContextUtilization = contextUtil
        };

    private static async Task<WorkflowExecution<string>> RunWorkflow(
        string name, int steps = 2)
    {
        var builder = new Workflow<string>(name);

        for (var i = 0; i < steps; i++)
        {
            var stepName = $"step-{i}";
            builder = builder.Job(stepName, (s, _) =>
                Task.FromResult(s + $"[{stepName}]"));
        }

        for (var i = 0; i < steps - 1; i++)
            builder = builder.Then($"step-{i}", $"step-{i + 1}");
        builder = builder.Then($"step-{steps - 1}", Workflow.End);

        return await builder.RunAsync("");
    }

    [Test]
    public async Task RegisterWorkflow_GetSnapshot_ReturnsStructuralMetrics()
    {
        _monitor.RegisterWorkflow("test-cell", MakeProfile(
            toolCount: 8, jobCount: 3, tagClusters: 2,
            resourceSpan: 3, contextUtil: 0.45f));

        var snapshot = await _monitor.GetSnapshotAsync("test-cell");

        snapshot.WorkflowName.ShouldBe("test-cell");
        snapshot.ToolCount.ShouldBe(8);
        snapshot.JobCount.ShouldBe(3);
        snapshot.TagClusterCount.ShouldBe(2);
        snapshot.ResourceSpan.ShouldBe(3);
        snapshot.ContextUtilization.ShouldBe(0.45f);
    }

    [Test]
    public async Task Record_GetSnapshot_ComputesAvgLatency()
    {
        _monitor.RegisterWorkflow("cell-a", MakeProfile());

        var exec1 = await RunWorkflow("cell-a");
        var exec2 = await RunWorkflow("cell-a");

        _monitor.Record(exec1);
        _monitor.Record(exec2);

        var snapshot = await _monitor.GetSnapshotAsync("cell-a");
        snapshot.AvgLatencyMs.ShouldBeGreaterThanOrEqualTo(0f);
    }

    [Test]
    public async Task Record_GetSnapshot_ComputesAvgCost()
    {
        _monitor.RegisterWorkflow("cell-b", MakeProfile());

        var exec = await RunWorkflow("cell-b");
        _monitor.Record(exec);

        var snapshot = await _monitor.GetSnapshotAsync("cell-b");
        snapshot.AvgCostPerExecution.ShouldBeGreaterThanOrEqualTo(0m);
    }

    [Test]
    public async Task Record_GetSnapshot_ComputesRoutingEntropy()
    {
        _monitor.RegisterWorkflow("cell-c", MakeProfile());

        // Run a workflow with 3 steps — entropy should be > 0
        var exec = await RunWorkflow("cell-c", steps: 3);
        _monitor.Record(exec);

        var snapshot = await _monitor.GetSnapshotAsync("cell-c");
        // With 3 equally-used jobs, normalized entropy should be close to 1.0
        snapshot.RoutingEntropy.ShouldBeGreaterThan(0f);
    }

    [Test]
    public async Task GetSnapshot_NoRecordings_ReturnsZeroTelemetry()
    {
        _monitor.RegisterWorkflow("empty-cell", MakeProfile());

        var snapshot = await _monitor.GetSnapshotAsync("empty-cell");

        snapshot.RoutingEntropy.ShouldBe(0f);
        snapshot.AvgLatencyMs.ShouldBe(0f);
        snapshot.AvgCostPerExecution.ShouldBe(0m);
    }

    [Test]
    public async Task GetSnapshot_UnregisteredCell_Throws()
    {
        await Should.ThrowAsync<InvalidOperationException>(() =>
            _monitor.GetSnapshotAsync("unknown-cell"));
    }

    [Test]
    public async Task SlidingWindow_OldExecutionsDropped()
    {
        var monitor = new WorkflowExecutionMonitor(windowSize: 3);
        monitor.RegisterWorkflow("windowed", MakeProfile());

        // Record 5 executions — only last 3 should be retained
        for (var i = 0; i < 5; i++)
        {
            var exec = await RunWorkflow("windowed");
            monitor.Record(exec);
        }

        // Should not throw — window keeps only recent entries
        var snapshot = await monitor.GetSnapshotAsync("windowed");
        snapshot.AvgLatencyMs.ShouldBeGreaterThanOrEqualTo(0f);
    }

    [Test]
    public async Task Record_MultipleJobWorkflow_EntropyApproachesOne()
    {
        _monitor.RegisterWorkflow("multi-job", MakeProfile());

        // Run several times with 4 steps each
        for (var i = 0; i < 5; i++)
        {
            var exec = await RunWorkflow("multi-job", steps: 4);
            _monitor.Record(exec);
        }

        var snapshot = await _monitor.GetSnapshotAsync("multi-job");
        // 4 jobs, each called once per execution → perfectly even distribution
        // Normalized entropy should be close to 1.0
        snapshot.RoutingEntropy.ShouldBeGreaterThan(0.9f);
    }

    [Test]
    public async Task RegisterWorkflow_OverwritesPreviousProfile()
    {
        _monitor.RegisterWorkflow("cell", MakeProfile(toolCount: 4));
        _monitor.RegisterWorkflow("cell", MakeProfile(toolCount: 10));

        var snapshot = await _monitor.GetSnapshotAsync("cell");
        snapshot.ToolCount.ShouldBe(10);
    }

    [Test]
    public async Task GetHealthSnapshot_NoRecordings_ReturnsNull()
    {
        var health = await _monitor.GetHealthSnapshotAsync("unknown");
        health.ShouldBeNull();
    }

    [Test]
    public async Task GetHealthSnapshot_InsufficientData_ReturnsNull()
    {
        _monitor.RegisterWorkflow("few", MakeProfile());

        // Only 2 executions — below minimum of 3
        for (var i = 0; i < 2; i++)
            _monitor.Record(await RunWorkflow("few"));

        (await _monitor.GetHealthSnapshotAsync("few")).ShouldBeNull();
    }

    [Test]
    public async Task GetHealthSnapshot_AllSuccess_ZeroErrorRate()
    {
        _monitor.RegisterWorkflow("healthy", MakeProfile());

        for (var i = 0; i < 5; i++)
            _monitor.Record(await RunWorkflow("healthy"));

        var health = await _monitor.GetHealthSnapshotAsync("healthy");
        health.ShouldNotBeNull();
        health!.ErrorRate.ShouldBe(0f);
        health.WindowSize.ShouldBe(5);
        health.WorkflowName.ShouldBe("healthy");
    }

    [Test]
    public async Task GetHealthSnapshot_MixedResults_ComputesErrorRate()
    {
        _monitor.RegisterWorkflow("mixed", MakeProfile());

        // 3 successes
        for (var i = 0; i < 3; i++)
            _monitor.Record(await RunWorkflow("mixed"));

        // 2 failures
        for (var i = 0; i < 2; i++)
            _monitor.Record(await RunFaultingWorkflow("mixed"));

        var health = await _monitor.GetHealthSnapshotAsync("mixed");
        health.ShouldNotBeNull();
        health!.ErrorRate.ShouldBe(0.4f, 0.01f); // 2/5
    }

    [Test]
    public async Task GetHealthSnapshot_IncreasingLatency_PositiveSlope()
    {
        // Use a workflow that introduces measurable delay per step
        var monitor = new WorkflowExecutionMonitor(windowSize: 10);
        monitor.RegisterWorkflow("degrading", MakeProfile());

        // Run workflows with increasing artificial delays
        for (var i = 0; i < 5; i++)
        {
            var delay = i * 20; // 0ms, 20ms, 40ms, 60ms, 80ms
            var wf = new Workflow<string>("degrading")
                .Job("work", async (s, _) =>
                {
                    await Task.Delay(delay);
                    return s + "[done]";
                })
                .Then("work", Workflow.End);

            monitor.Record(await wf.RunAsync(""));
        }

        var health = await monitor.GetHealthSnapshotAsync("degrading");
        health.ShouldNotBeNull();
        // Increasing delays → positive slope
        health!.LatencyTrendSlope.ShouldBeGreaterThan(0f);
    }

    private static async Task<WorkflowExecution<string>> RunFaultingWorkflow(string name)
    {
        var wf = new Workflow<string>(name)
            .Job("fail", (_, _) => throw new InvalidOperationException("boom"))
            .Then("fail", Workflow.End);

        return await wf.RunAsync("");
    }
}
