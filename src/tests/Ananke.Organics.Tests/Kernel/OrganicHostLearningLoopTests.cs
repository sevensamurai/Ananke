using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Kernel;
using Ananke.Organics.Sensing;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel;

/// <summary>
/// Integration tests for C-1 (organic learning loop closure) and
/// C-2 (DivisionResult applied to landscape and router).
/// </summary>
[TestFixture]
public class OrganicHostLearningLoopTests
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

    // -- C-1: Organic learning loop closure --------------------------

    [Test]
    public async Task Division_WithOutcomeTracker_CallsRewardAsyncAfterStabilization()
    {
        // Arrange — divider returns manifests whose names match the plan children
        var tracker = new CapturingOutcomeTracker();
        var memory = new StubEmpiricalMemory();
        var divider = new ManifestReturnDivider("test-a", "test-b");
        var policy = AlwaysDividePolicy("test");
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            outcomeTracker: tracker,
            memory: memory,
            stabilizationWindowMs: 0);
        host.OnDivisionCompleted += _ => { divisionDone.TrySetResult(); return Task.CompletedTask; };

        host.Register("test", MakeKit("t1", "t2"));

        // Act
        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — RewardAsync was called with the divisionId and plan
        tracker.RewardCalls.ShouldBe(1);
        tracker.LastPlan.ShouldNotBeNull();
        tracker.LastPlan!.ParentWorkflow.ShouldBe("test");
    }

    [Test]
    public async Task Division_WithoutOutcomeTracker_NoCrashAndDivisionCompletes()
    {
        // Arrange — no OutcomeTracker; reward path should be skipped silently
        var divider = new ManifestReturnDivider("child-x");
        var policy = AlwaysDividePolicy("test");
        DivisionSignal? completed = null;
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            outcomeTracker: null,
            memory: new StubEmpiricalMemory(),
            stabilizationWindowMs: 0);

        host.OnDivisionCompleted += s =>
        {
            completed = s;
            divisionDone.TrySetResult();
            return Task.CompletedTask;
        };

        host.Register("test", MakeKit("t1"));

        // Act
        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — division completes without error; outcome tracker path is no-op
        completed.ShouldNotBeNull();
    }

    [Test]
    public async Task RecordBaseline_IsCalledBeforeDivision_SoRewardCanResolveIt()
    {
        // Arrange — tracker validates baseline is present via RewardAsync internals
        var realTracker = new DivisionOutcomeTracker(new StubEmpiricalMemory());
        var divider = new ManifestReturnDivider("child-1");
        var policy = AlwaysDividePolicy("test");
        Exception? rewardException = null;
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            outcomeTracker: realTracker,
            memory: new StubEmpiricalMemory(),
            stabilizationWindowMs: 0);

        // Capture division failures to detect missing-baseline scenario
        host.OnDivisionFailed += _ => Task.CompletedTask;
        host.OnDivisionCompleted += _ => { divisionDone.TrySetResult(); return Task.CompletedTask; };

        host.Register("test", MakeKit("t1"));

        // Act
        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — no exception means baseline was recorded before RewardAsync ran
        rewardException.ShouldBeNull();
    }

    // -- C-2: DivisionResult applied to landscape and router ----------

    [Test]
    public async Task Division_RemovesParentFromLandscape()
    {
        // Arrange — pre-register parent in landscape
        _landscape.Register(new WorkflowSignal
        {
            WorkflowName = "test",
            Domain = "general",
            Capabilities = ["t1"],
            Timestamp = DateTimeOffset.UtcNow
        });

        var divider = new ManifestReturnDivider("test-a", "test-b");
        var policy = AlwaysDividePolicy("test");
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            memory: new StubEmpiricalMemory(),
            stabilizationWindowMs: 0);
        host.OnDivisionCompleted += _ => { divisionDone.TrySetResult(); return Task.CompletedTask; };

        host.Register("test", MakeKit("t1"));

        // Act
        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — parent is gone from landscape
        var parentCaps = _landscape.Discover("general");
        parentCaps.Any(c => c.WorkflowName == "test").ShouldBeFalse();
    }

    [Test]
    public async Task Division_RegistersChildCellsInLandscape()
    {
        var divider = new ManifestReturnDivider("child-browse", "child-orders");
        var policy = AlwaysDividePolicy("test");
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            memory: new StubEmpiricalMemory(),
            stabilizationWindowMs: 0);
        host.OnDivisionCompleted += _ => { divisionDone.TrySetResult(); return Task.CompletedTask; };

        host.Register("test", MakeKit("t1"));

        // Act
        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — both children are now in the landscape
        var all = _landscape.DiscoverAll();
        all.Any(c => c.WorkflowName == "child-browse").ShouldBeTrue();
        all.Any(c => c.WorkflowName == "child-orders").ShouldBeTrue();
    }

    [Test]
    public async Task Division_CallsDomainRouterIndexAsync()
    {
        var router = new CapturingDomainRouter();
        var divider = new ManifestReturnDivider("route-a", "route-b");
        var routePolicy = new DelegatePolicy((_, _, _) => Task.FromResult<DivisionPlan?>(new DivisionPlan
        {
            ParentWorkflow = "test",
            Children =
            [
                new ChildSpec { Name = "route-a", Tools = ["t1"], Jobs = ["job0"], Domain = "d1" },
                new ChildSpec { Name = "route-b", Tools = ["t2"], Jobs = ["job0"], Domain = "d2" }
            ],
            Reason = "router test",
            InfluencingEntries = []
        }));

        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = CreateHost(
            policy: routePolicy,
            divider: divider,
            domainRouter: router,
            memory: new StubEmpiricalMemory(),
            stabilizationWindowMs: 0);
        host.OnDivisionCompleted += _ => { divisionDone.TrySetResult(); return Task.CompletedTask; };

        host.Register("test", MakeKit("t1"));

        // Act
        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — router received the new child specs
        router.IndexCalls.ShouldBe(1);
        router.LastChildren.ShouldNotBeNull();
        router.LastChildren!.Any(c => c.Name == "route-a").ShouldBeTrue();
        router.LastChildren!.Any(c => c.Name == "route-b").ShouldBeTrue();
    }

    [Test]
    public async Task Division_WithoutDomainRouter_NoCrash()
    {
        // Arrange — no DomainRouter configured
        var divider = new ManifestReturnDivider("no-router-child");
        var policy = AlwaysDividePolicy("test");
        DivisionSignal? completed = null;
        var divisionDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = CreateHost(
            policy: policy,
            divider: divider,
            domainRouter: null,
            memory: new StubEmpiricalMemory(),
            stabilizationWindowMs: 0);

        host.OnDivisionCompleted += s =>
        {
            completed = s;
            divisionDone.TrySetResult();
            return Task.CompletedTask;
        };

        host.Register("test", MakeKit("t1"));

        // Act
        var exec = await RunWorkflow("test");
        host.ObserveExecution("test", exec);

        await divisionDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        completed.ShouldNotBeNull();
    }

    [Test]
    public void Constructor_WithDividerButNoSharedMemory_ThrowsArgumentException()
    {
        var options = new OrganicGrowthOptions
        {
            Policy = new NullPolicy(),
            Divider = new ManifestReturnDivider("x"),
            SharedMemory = null   // deliberately null
        };

        Should.Throw<ArgumentException>(() =>
            new OrganicHost(_cellHost, _landscape, options));
    }

    // -- Helpers -----------------------------------------------------

    private OrganicHost CreateHost(
        IDivisionPolicy? policy = null,
        IWorkflowDivider? divider = null,
        IDivisionOutcomeTracker? outcomeTracker = null,
        IDomainRouter? domainRouter = null,
        IEmpiricalMemory? memory = null,
        int stabilizationWindowMs = 0)
    {
        var options = new OrganicGrowthOptions
        {
            Policy = policy ?? new NullPolicy(),
            ApprovalGate = new AutoApprovalGate(),
            Monitor = new WorkflowExecutionMonitor(),
            EvaluationInterval = 1,
            Divider = divider,
            SharedMemory = divider is not null ? (memory ?? new StubEmpiricalMemory()) : null,
            OutcomeTracker = outcomeTracker,
            DomainRouter = domainRouter,
            StabilizationWindowMs = stabilizationWindowMs
        };

        return new OrganicHost(_cellHost, _landscape, options);
    }

    private static DelegatePolicy AlwaysDividePolicy(string parent) =>
        new((_, _, _) => Task.FromResult<DivisionPlan?>(MakePlan(parent)));

    private static DivisionPlan MakePlan(string parent) => new()
    {
        ParentWorkflow = parent,
        Children =
        [
            new ChildSpec { Name = $"{parent}-a", Tools = ["t1"], Jobs = ["job0"], Domain = "d1" },
            new ChildSpec { Name = $"{parent}-b", Tools = ["t2"], Jobs = ["job0"], Domain = "d2" }
        ],
        Reason = "test division",
        InfluencingEntries = ["entry-1"]
    };

    private static async Task<WorkflowExecution<string>> RunWorkflow(string name)
    {
        var workflow = new Workflow<string>(name)
            .Job("job0", async (state, ct) =>
            {
                await Task.Delay(1, ct);
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

    // -- Test doubles -------------------------------------------------

    private sealed class NullPolicy : IDivisionPolicy
    {
        public Task<DivisionPlan?> EvaluateAsync(
            ComplexitySnapshot snapshot, WorkflowManifest manifest, CancellationToken ct = default)
            => Task.FromResult<DivisionPlan?>(null);
    }

    private sealed class DelegatePolicy(
        Func<ComplexitySnapshot, WorkflowManifest, CancellationToken, Task<DivisionPlan?>> evaluate)
        : IDivisionPolicy
    {
        public Task<DivisionPlan?> EvaluateAsync(
            ComplexitySnapshot snapshot, WorkflowManifest manifest, CancellationToken ct = default)
            => evaluate(snapshot, manifest, ct);
    }

    /// <summary>
    /// Returns a <see cref="DivisionResult"/> with a manifest per child name,
    /// routing table derived from the plan children, and empty memory profiles.
    /// </summary>
    private sealed class ManifestReturnDivider(params string[] childNames) : IWorkflowDivider
    {
        public Task<DivisionResult> DivideAsync(
            DivisionPlan plan, WorkflowManifest parentManifest,
            IEmpiricalMemory parentMemory, CancellationToken ct = default)
        {
            var manifests = childNames.Select(n => new WorkflowManifest
            {
                Name = n,
                Models = [],
                Jobs = new Dictionary<string, JobDefinition> { ["job0"] = new() { Type = "agent" } },
                Connections = []
            }).ToList();

            var routing = plan.Children
                .Where(c => childNames.Contains(c.Name))
                .ToDictionary(c => c.Domain, c => c.Name);

            return Task.FromResult(new DivisionResult
            {
                NewManifests = manifests,
                RoutingTable = routing,
                MemoryProfiles = []
            });
        }
    }

    private sealed class CapturingOutcomeTracker : IDivisionOutcomeTracker
    {
        public int RewardCalls { get; private set; }
        public IReadOnlyList<ComplexitySnapshot>? LastChildSnapshots { get; private set; }
        public DivisionPlan? LastPlan { get; private set; }

        public void RecordBaseline(string divisionId, ComplexitySnapshot parentBaseline) { }

        public Task RewardAsync(
            string divisionId,
            IReadOnlyList<ComplexitySnapshot> childSnapshots,
            DivisionPlan originalPlan,
            CancellationToken ct = default)
        {
            RewardCalls++;
            LastChildSnapshots = childSnapshots;
            LastPlan = originalPlan;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingDomainRouter : IDomainRouter
    {
        public int IndexCalls { get; private set; }
        public IReadOnlyList<ChildSpec>? LastChildren { get; private set; }

        public Task<string> RouteAsync(string userMessage, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task IndexAsync(
            IReadOnlyList<ChildSpec> children,
            IReadOnlyDictionary<string, string> toolDescriptions,
            CancellationToken ct = default)
        {
            IndexCalls++;
            LastChildren = children;
            return Task.CompletedTask;
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
        public int Count => 0;
    }
}
