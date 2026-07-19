using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Sensing;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel;

[TestFixture]
public class OrganicHostTests
{
    private InProcessWorkflowHost _cellHost = null!;
    private InMemoryCapabilityMap _landscape = null!;

    [SetUp]
    public void SetUp()
    {
        _cellHost = new InProcessWorkflowHost();
        _landscape = new InMemoryCapabilityMap(TimeSpan.FromSeconds(30));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _cellHost.DisposeAsync();
    }

    // ── Construction guards ──────────────────────────────────────────

    [Test]
    public void Constructor_DividerSetButSharedMemoryNull_ThrowsArgumentException()
    {
        var options = new OrganicGrowthOptions
        {
            Policy = new NullPolicy(),
            Divider = new StubWorkflowDivider(),
            SharedMemory = null   // missing — should throw
        };

        Should.Throw<ArgumentException>(() =>
            new OrganicHost(_cellHost, _landscape, options));
    }

    [Test]
    public async Task Constructor_DividerAndSharedMemoryBothSet_DoesNotThrow()
    {
        var options = new OrganicGrowthOptions
        {
            Policy = new NullPolicy(),
            Divider = new StubWorkflowDivider(),
            SharedMemory = new StubEmpiricalMemory()
        };

        await using var host = new OrganicHost(_cellHost, _landscape, options);
        // No exception — passes
    }

    // ── Registration ─────────────────────────────────────────────────

    [Test]
    public async Task Register_SetsStructuralProfile()
    {
        var monitor = new WorkflowExecutionMonitor();
        await using var host = CreateHost(monitor: monitor);
        var kit = MakeKit("search_web", "search_db");

        host.Register("test-workflow", kit);

        var snapshot = await monitor.GetSnapshotAsync("test-workflow");
        snapshot.ToolCount.ShouldBe(2);
    }

    [Test]
    public async Task Register_NullToolKit_RegistersMinimalProfile()
    {
        var monitor = new WorkflowExecutionMonitor();
        await using var host = CreateHost(monitor: monitor);

        host.Register("test-workflow", null);

        var snapshot = await monitor.GetSnapshotAsync("test-workflow");
        snapshot.ToolCount.ShouldBe(0);
    }

    // ── ObserveExecution ─────────────────────────────────────────────

    [Test]
    public async Task ObserveExecution_RecordsInMonitor()
    {
        var monitor = new WorkflowExecutionMonitor();
        await using var host = CreateHost(monitor: monitor);
        host.Register("test", MakeKit("a"));

        var execution = await RunWorkflow("test");
        host.ObserveExecution("test", execution);

        // Wait for background loop to process
        await WaitForProcessing(host, "test");

        var snapshot = await monitor.GetSnapshotAsync("test");
        snapshot.AvgLatencyMs.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task ObserveExecution_RecordsFaultedForHealth()
    {
        var monitor = new WorkflowExecutionMonitor();
        await using var host = CreateHost(monitor: monitor);
        host.Register("test", MakeKit("a"));

        // Need at least 3 executions for health snapshot
        for (var i = 0; i < 3; i++)
        {
            var execution = await RunFaultingWorkflow("test");
            host.ObserveExecution("test", execution);
            await WaitForProcessing(host, "test");
        }

        // Faulted executions ARE recorded for health monitoring
        var health = await monitor.GetHealthSnapshotAsync("test");
        health.ShouldNotBeNull();
        health!.ErrorRate.ShouldBe(1.0f);
    }

    [Test]
    public async Task ObserveExecution_EvaluatesPolicyAtInterval()
    {
        var evaluationCount = 0;
        var policy = new DelegatePolicy((_, _, _) =>
        {
            Interlocked.Increment(ref evaluationCount);
            return Task.FromResult<DivisionPlan?>(null);
        });

        await using var host = CreateHost(policy: policy, evaluationInterval: 3);
        host.Register("test", MakeKit("a"));

        for (var i = 0; i < 6; i++)
        {
            var exec = await RunWorkflow("test");
            host.ObserveExecution("test", exec);
            await WaitForProcessing(host, "test");
        }

        evaluationCount.ShouldBe(2); // at count 3 and 6
    }

    // ── Division signaling ───────────────────────────────────────────

    [Test]
    public async Task PolicyTriggers_CallsApprovalGate()
    {
        var gateCalled = false;
        var gate = new CallbackApprovalGate((_, _, _) =>
        {
            gateCalled = true;
            return Task.FromResult(DivisionApproval.Reject("test", "test"));
        });

        var policy = new DelegatePolicy((_, _, _) =>
            Task.FromResult<DivisionPlan?>(MakePlan("test")));

        await using var host = CreateHost(policy: policy, gate: gate, evaluationInterval: 1);
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await WaitForProcessing(host, "test");

        gateCalled.ShouldBeTrue();
    }

    [Test]
    public async Task PolicyTriggers_EmitsOnDivisionProposed()
    {
        DivisionSignal? proposedSignal = null;
        var policy = new DelegatePolicy((_, _, _) =>
            Task.FromResult<DivisionPlan?>(MakePlan("test")));

        await using var host = CreateHost(policy: policy, evaluationInterval: 1);
        host.OnDivisionProposed += signal =>
        {
            proposedSignal = signal;
            return Task.CompletedTask;
        };
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await WaitForProcessing(host, "test");

        proposedSignal.ShouldNotBeNull();
        proposedSignal.WorkflowName.ShouldBe("test");
        proposedSignal.Approval.ShouldBeNull(); // not yet reviewed
    }

    [Test]
    public async Task GateApproves_EmitsOnDivisionApproved()
    {
        DivisionSignal? approvedSignal = null;
        var policy = new DelegatePolicy((_, _, _) =>
            Task.FromResult<DivisionPlan?>(MakePlan("test")));

        await using var host = CreateHost(policy: policy, evaluationInterval: 1);
        host.OnDivisionApproved += signal =>
        {
            approvedSignal = signal;
            return Task.CompletedTask;
        };
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await WaitForProcessing(host, "test");

        approvedSignal.ShouldNotBeNull();
        approvedSignal.Approval.ShouldNotBeNull();
        approvedSignal.Approval!.IsApproved.ShouldBeTrue();
    }

    [Test]
    public async Task GateRejects_EmitsOnDivisionRejected()
    {
        DivisionSignal? rejectedSignal = null;
        var gate = new CallbackApprovalGate((_, _, _) =>
            Task.FromResult(DivisionApproval.Reject("nope", "operator")));
        var policy = new DelegatePolicy((_, _, _) =>
            Task.FromResult<DivisionPlan?>(MakePlan("test")));

        await using var host = CreateHost(policy: policy, gate: gate, evaluationInterval: 1);
        host.OnDivisionRejected += signal =>
        {
            rejectedSignal = signal;
            return Task.CompletedTask;
        };
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await WaitForProcessing(host, "test");

        rejectedSignal.ShouldNotBeNull();
        rejectedSignal.Approval.ShouldNotBeNull();
        rejectedSignal.Approval!.IsApproved.ShouldBeFalse();
    }

    // ── Remote polling ─────────────────────────────────────────────

    [Test]
    public async Task RemotePolling_EvaluatesPolicyForRemoteCells()
    {
        var evaluatedNames = new List<string>();
        var policy = new DelegatePolicy((snapshot, _, _) =>
        {
            lock (evaluatedNames) evaluatedNames.Add(snapshot.WorkflowName);
            return Task.FromResult<DivisionPlan?>(null);
        });

        var monitor = new WorkflowExecutionMonitor();
        monitor.RegisterWorkflow("remote-cell", new StructuralProfile
        {
            ToolCount = 4,
            JobCount = 1,
            TagClusterCount = 1,
            ResourceSpan = 2,
            ContextUtilization = 0.3f
        });

        var source = new StubRemoteCellSource(["remote-cell"]);

        await using var host = CreateHost(
            policy: policy,
            monitor: monitor,
            remoteCellSource: source,
            remotePollingInterval: TimeSpan.FromMilliseconds(50));

        await host.WhenEvaluatedAsync("remote-cell").WaitAsync(TimeSpan.FromSeconds(5));

        lock (evaluatedNames)
        {
            evaluatedNames.ShouldContain("remote-cell");
        }
    }

    [Test]
    public async Task RemotePolling_ProposalEmitsEvent()
    {
        DivisionSignal? proposedSignal = null;
        var policy = new DelegatePolicy((_, _, _) =>
            Task.FromResult<DivisionPlan?>(MakePlan("remote-cell")));

        var monitor = new WorkflowExecutionMonitor();
        monitor.RegisterWorkflow("remote-cell", new StructuralProfile
        {
            ToolCount = 4,
            JobCount = 1,
            TagClusterCount = 1,
            ResourceSpan = 2,
            ContextUtilization = 0.3f
        });

        var source = new StubRemoteCellSource(["remote-cell"]);

        await using var host = CreateHost(
            policy: policy,
            monitor: monitor,
            remoteCellSource: source,
            remotePollingInterval: TimeSpan.FromMilliseconds(50));

        host.OnDivisionProposed += signal =>
        {
            proposedSignal = signal;
            return Task.CompletedTask;
        };

        await host.WhenEvaluatedAsync("remote-cell").WaitAsync(TimeSpan.FromSeconds(5));

        proposedSignal.ShouldNotBeNull();
        proposedSignal.WorkflowName.ShouldBe("remote-cell");
    }

    [Test]
    public async Task RemotePolling_NoSourceConfigured_NoPollTaskCreated()
    {
        // Verify that a host without RemoteCellSource disposes cleanly
        // (no polling task to await). The host is disposed by going out of scope.
        await using var host = CreateHost();
    }

    // ── CellHost exposure ────────────────────────────────────────────

    [Test]
    public async Task CellHost_ExposesInnerHost()
    {
        await using var host = CreateHost();

        host.CellHost.ShouldBeSameAs(_cellHost);
    }

    // ── Dispose ──────────────────────────────────────────────────────

    [Test]
    public async Task DisposeAsync_StopsBackgroundLoop()
    {
        var host = CreateHost();

        await host.DisposeAsync();

        // After dispose, writing to queue should be a no-op (channel completed)
        // and no exception should be thrown
        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec); // should not throw
    }

    [Test]
    public async Task DisposeAsync_DisposesCellHost()
    {
        var host = CreateHost();
        await _cellHost.StartAsync("alive", WorkflowLoops.Park);

        await host.DisposeAsync();

        _cellHost.ListActive().ShouldBeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private OrganicHost CreateHost(
        IDivisionPolicy? policy = null,
        IDivisionApprovalGate? gate = null,
        IHealthMonitor? monitor = null,
        int evaluationInterval = 10,
        IRemoteCellSource? remoteCellSource = null,
        TimeSpan? remotePollingInterval = null)
    {
        var options = new OrganicGrowthOptions
        {
            Policy = policy ?? new NullPolicy(),
            ApprovalGate = gate ?? new AutoApprovalGate(),
            Monitor = monitor ?? new WorkflowExecutionMonitor(),
            EvaluationInterval = evaluationInterval,
            RemoteCellSource = remoteCellSource
        };

        if (remotePollingInterval.HasValue)
            options = options with { RemotePollingInterval = remotePollingInterval.Value };

        return new OrganicHost(_cellHost, _landscape, options);
    }

    private static async Task<WorkflowExecution<string>> RunWorkflow(string name, int steps = 2)
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

        // Chain jobs
        for (var i = 0; i < steps - 1; i++)
            workflow.Then($"job{i}", $"job{i + 1}");
        workflow.Then($"job{steps - 1}", Workflow.End);

        return await workflow.RunAsync("start");
    }

    private static async Task<WorkflowExecution<string>> RunFaultingWorkflow(string name)
    {
        var workflow = new Workflow<string>(name)
            .Job("fail", (_, _) => throw new InvalidOperationException("boom"))
            .Then("fail", Workflow.End);

        return await workflow.RunAsync("start");
    }

    private static ToolKit MakeKit(params string[] toolNames)
    {
        var kit = new ToolKit("test");
        foreach (var n in toolNames)
            kit.AddTool(n, $"Description for {n}", () => ToolResult.Ok("ok"));
        return kit;
    }

    private static DivisionPlan MakePlan(string parent) => new()
    {
        ParentWorkflow = parent,
        Children = [new ChildSpec { Name = $"{parent}-a", Tools = ["t1"], Jobs = ["j1"], Domain = "d1" }],
        Reason = "Test division"
    };

    private static async Task WaitForProcessing(OrganicHost host, string workflowName)
    {
        await host.WhenProcessedAsync(workflowName).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ── Test doubles ─────────────────────────────────────────────────

    private sealed class NullPolicy : IDivisionPolicy
    {
        public Task<DivisionPlan?> EvaluateAsync(
            ComplexitySnapshot snapshot, WorkflowManifest manifest,
            CancellationToken ct = default)
            => Task.FromResult<DivisionPlan?>(null);
    }

    private sealed class DelegatePolicy(
        Func<ComplexitySnapshot, WorkflowManifest, CancellationToken, Task<DivisionPlan?>> evaluate)
        : IDivisionPolicy
    {
        public Task<DivisionPlan?> EvaluateAsync(
            ComplexitySnapshot snapshot, WorkflowManifest manifest,
            CancellationToken ct = default)
            => evaluate(snapshot, manifest, ct);
    }

    private sealed class StubRemoteCellSource(IReadOnlyList<string> names) : IRemoteCellSource
    {
        public Task<IReadOnlyList<string>> GetRemoteCellNamesAsync(CancellationToken ct = default)
            => Task.FromResult(names);
    }

    private sealed class StubWorkflowDivider : IWorkflowDivider
    {
        public Task<DivisionResult> DivideAsync(
            DivisionPlan plan, WorkflowManifest parentManifest,
            IEmpiricalMemory parentMemory, CancellationToken ct = default)
            => Task.FromResult(new DivisionResult
            {
                NewManifests = [],
                RoutingTable = new Dictionary<string, string>(),
                MemoryProfiles = []
            });
    }

    private sealed class StubEmpiricalMemory : IEmpiricalMemory
    {
        public Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default) =>
            Task.FromResult(entry);
        public Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
            string situation, RecallOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmpiricalMatch>>([]);
        public Task ReinforceAsync(string entryId, Reinforcement reinforcement, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task ContradictAsync(string entryId, string reason, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<EmpiricalEntry?> GetAsync(string entryId, CancellationToken ct = default) =>
            Task.FromResult<EmpiricalEntry?>(null);
        public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
            int offset, int limit, EmpiricalKind? kind = null,
            string? entityId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmpiricalEntry>>([]);
        public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
            BrowseOptions options, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmpiricalEntry>>([]);
        public Task<int> CountAsync(BrowseOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task MarkConsolidatedAsync(string entryId, string documentId, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<EmpiricalMatch>> PairRecallAsync(
            EmpiricalEntry reference, PairRecallOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmpiricalMatch>>([]);
    }
}
