using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Sensing;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel;

[TestFixture]
public class OrganicHostDivisionTests
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

    // -- Division execution ------------------------------------------

    [Test]
    public async Task ApprovedDivision_WithDivider_ExecutesDivision()
    {
        var dividerCalled = false;
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var divider = new StubDivider(() => dividerCalled = true);
        var policy = AlwaysDividePolicy("test");

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            evaluationInterval: 1);
        host.OnDivisionCompleted += _ => { divisionDone.TrySetResult(); return Task.CompletedTask; };
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        dividerCalled.ShouldBeTrue();
    }

    [Test]
    public async Task ApprovedDivision_WithoutDivider_EmitsEventOnly()
    {
        DivisionSignal? approvedSignal = null;
        var policy = AlwaysDividePolicy("test");

        await using var host = CreateHost(
            policy: policy,
            divider: null,
            evaluationInterval: 1);
        host.OnDivisionApproved += signal =>
        {
            approvedSignal = signal;
            return Task.CompletedTask;
        };
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await WaitForProcessing(host, "test");

        // Event fires, no divider called (no exception, no crash)
        approvedSignal.ShouldNotBeNull();
        approvedSignal.Approval!.IsApproved.ShouldBeTrue();
    }

    [Test]
    public async Task ApprovedDivision_WithDivider_EmitsOnDivisionCompleted()
    {
        DivisionSignal? completedSignal = null;
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var divider = new StubDivider();
        var policy = AlwaysDividePolicy("test");

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            evaluationInterval: 1);
        host.OnDivisionCompleted += signal =>
        {
            completedSignal = signal;
            divisionDone.TrySetResult();
            return Task.CompletedTask;
        };
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        completedSignal.ShouldNotBeNull();
        completedSignal.WorkflowName.ShouldBe("test");
    }

    [Test]
    public async Task DivisionFailed_EmitsOnDivisionFailed()
    {
        DivisionSignal? failedSignal = null;
        var divisionFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var divider = new FailingDivider();
        var policy = AlwaysDividePolicy("test");

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            evaluationInterval: 1);
        host.OnDivisionFailed += signal =>
        {
            failedSignal = signal;
            divisionFailed.TrySetResult();
            return Task.CompletedTask;
        };
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        failedSignal.ShouldNotBeNull();
        failedSignal.WorkflowName.ShouldBe("test");
    }

    [Test]
    public async Task RevisedPlan_UsedForExecution()
    {
        DivisionPlan? executedPlan = null;
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var divider = new CapturingDivider(plan => executedPlan = plan);
        var revisedPlan = MakePlan("test") with
        {
            Reason = "revised by reviewer"
        };
        var gate = new CallbackApprovalGate((_, _, _) =>
            Task.FromResult(DivisionApproval.Revise(revisedPlan, "revised", "reviewer")));
        var policy = AlwaysDividePolicy("test");

        await using var host = CreateHost(
            policy: policy,
            gate: gate,
            divider: divider,
            evaluationInterval: 1);
        host.OnDivisionCompleted += _ => { divisionDone.TrySetResult(); return Task.CompletedTask; };
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        executedPlan.ShouldNotBeNull();
        executedPlan.Reason.ShouldBe("revised by reviewer");
    }

    [Test]
    public async Task RejectedDivision_DividerNotCalled()
    {
        var dividerCalled = false;
        var divider = new StubDivider(() => dividerCalled = true);
        var gate = new CallbackApprovalGate((_, _, _) =>
            Task.FromResult(DivisionApproval.Reject("nope", "operator")));
        var policy = AlwaysDividePolicy("test");

        await using var host = CreateHost(
            policy: policy,
            gate: gate,
            divider: divider,
            evaluationInterval: 1);
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await WaitForProcessing(host, "test");

        dividerCalled.ShouldBeFalse();
    }

    // -- Dispose awaits in-flight divisions --------------------------

    [Test]
    public async Task DisposeAsync_AwaitsInflightDivisionTask()
    {
        var divisionCompleted = false;
        var divider = new SlowDivider(
            delayMs: 150,
            onCompleted: () => divisionCompleted = true);
        var policy = AlwaysDividePolicy("test");

        var host = CreateHost(
            policy: policy,
            divider: divider,
            evaluationInterval: 1,
            divisionShutdownTimeoutMs: 5_000);
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        // Wait for the background loop to dequeue and start the division task
        await WaitForProcessing(host, "test");

        // Dispose should wait for the 150 ms division to finish
        await host.DisposeAsync();

        divisionCompleted.ShouldBeTrue();
    }

    [Test]
    public async Task DisposeAsync_TimesOut_DoesNotHangIndefinitely()
    {
        var divider = new SlowDivider(delayMs: 10_000); // longer than timeout
        var policy = AlwaysDividePolicy("test");

        var host = CreateHost(
            policy: policy,
            divider: divider,
            evaluationInterval: 1,
            divisionShutdownTimeoutMs: 200); // tiny timeout
        host.Register("test", MakeKit("a"));

        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await WaitForProcessing(host, "test");

        // Should complete quickly despite the stuck divider
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await host.DisposeAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.ShouldBeLessThan(3_000);
    }

    // -- Helpers -----------------------------------------------------

    private OrganicHost CreateHost(
        IDivisionPolicy? policy = null,
        IDivisionApprovalGate? gate = null,
        IWorkflowDivider? divider = null,
        int evaluationInterval = 10,
        int divisionShutdownTimeoutMs = 30_000)
    {
        var options = new OrganicGrowthOptions
        {
            Policy = policy ?? new NullPolicy(),
            ApprovalGate = gate ?? new AutoApprovalGate(),
            Monitor = new WorkflowExecutionMonitor(),
            EvaluationInterval = evaluationInterval,
            Divider = divider,
            SharedMemory = divider is not null ? new StubEmpiricalMemory() : null,
            DivisionShutdownTimeoutMs = divisionShutdownTimeoutMs
        };

        return new OrganicHost(_cellHost, _landscape, options);
    }

    private static DelegatePolicy AlwaysDividePolicy(string parent) =>
        new((_, _, _) => Task.FromResult<DivisionPlan?>(MakePlan(parent)));

    private static DivisionPlan MakePlan(string parent) => new()
    {
        ParentWorkflow = parent,
        Children = [new ChildSpec { Name = $"{parent}-a", Tools = ["t1"], Jobs = ["j1"], Domain = "d1" }],
        Reason = "Test division"
    };

    private static async Task<WorkflowExecution<string>> RunWorkflow(string name)
    {
        var workflow = new Workflow<string>(name)
            .Job("job0", async (state, ct) =>
            {
                await Task.Yield();
                return state + "[job0]";
            })
            .Then("job0", Workflow.End);

        return await workflow.RunAsync("start");
    }

    private static ToolKit MakeKit(params string[] toolNames)
    {
        var kit = new ToolKit("test");
        foreach (var n in toolNames)
            kit.AddTool(n, $"Description for {n}", () => ToolResult.Ok("ok"));
        return kit;
    }

    private static async Task WaitForProcessing(OrganicHost host, string workflowName)
    {
        await host.WhenProcessedAsync(workflowName).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // -- Test doubles ------------------------------------------------

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

    private sealed class StubDivider(Action? onCalled = null) : IWorkflowDivider
    {
        public Task<DivisionResult> DivideAsync(
            DivisionPlan plan, WorkflowManifest parentManifest,
            IEmpiricalMemory parentMemory, CancellationToken ct = default)
        {
            onCalled?.Invoke();
            return Task.FromResult(new DivisionResult
            {
                NewManifests = [],
                RoutingTable = new Dictionary<string, string>(),
                MemoryProfiles = []
            });
        }
    }

    private sealed class CapturingDivider(Action<DivisionPlan> onPlan) : IWorkflowDivider
    {
        public Task<DivisionResult> DivideAsync(
            DivisionPlan plan, WorkflowManifest parentManifest,
            IEmpiricalMemory parentMemory, CancellationToken ct = default)
        {
            onPlan(plan);
            return Task.FromResult(new DivisionResult
            {
                NewManifests = [],
                RoutingTable = new Dictionary<string, string>(),
                MemoryProfiles = []
            });
        }
    }

    private sealed class FailingDivider : IWorkflowDivider
    {
        public Task<DivisionResult> DivideAsync(
            DivisionPlan plan, WorkflowManifest parentManifest,
            IEmpiricalMemory parentMemory, CancellationToken ct = default)
            => throw new InvalidOperationException("Division failed");
    }

    private sealed class SlowDivider(int delayMs, Action? onCompleted = null) : IWorkflowDivider
    {
        public async Task<DivisionResult> DivideAsync(
            DivisionPlan plan, WorkflowManifest parentManifest,
            IEmpiricalMemory parentMemory, CancellationToken ct = default)
        {
            await Task.Delay(delayMs, CancellationToken.None);
            onCompleted?.Invoke();
            return new DivisionResult
            {
                NewManifests = [],
                RoutingTable = new Dictionary<string, string>(),
                MemoryProfiles = []
            };
        }
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
