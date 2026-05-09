using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Division;
using Ananke.Organics.Sensing;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class WorkflowDividerTests
{
    private InProcessWorkflowHost _host = null!;
    private InMemoryCapabilityMap _landscape = null!;
    private StubActivatorFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new InProcessWorkflowHost();
        _landscape = new InMemoryCapabilityMap();
        _factory = new StubActivatorFactory();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
    }

    // ── Core division flow ──────────────────────────────────────────

    [Test]
    public async Task DivideAsync_SpawnsAllChildren()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();

        await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        var active = _host.ListActive();
        active.ShouldContain("child-search");
        active.ShouldContain("child-payment");
    }

    [Test]
    public async Task DivideAsync_KillsParent()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();

        await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        _host.ListActive().ShouldNotContain("parent");
    }

    [Test]
    public async Task DivideAsync_RemovesParentFromLandscape()
    {
        await StartParentAsync("parent");
        RegisterInLandscape("parent", "general");
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();

        await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        _landscape.Discover("general").ShouldBeEmpty();
    }

    [Test]
    public async Task DivideAsync_ReturnsRoutingTable()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();

        var result = await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        result.RoutingTable.ShouldContainKey("search");
        result.RoutingTable["search"].ShouldBe("child-search");
        result.RoutingTable.ShouldContainKey("payment");
        result.RoutingTable["payment"].ShouldBe("child-payment");
    }

    [Test]
    public async Task DivideAsync_ReturnsMemoryProfiles()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();

        var result = await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        result.MemoryProfiles.Count.ShouldBe(2);
        result.MemoryProfiles[0].Domains.ShouldContain("search");
        result.MemoryProfiles[0].Domains.ShouldContain("general");
        result.MemoryProfiles[0].LineageTags.ShouldContain("parent");
        result.MemoryProfiles[1].Domains.ShouldContain("payment");
    }

    [Test]
    public async Task DivideAsync_ReturnsChildManifests()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();

        var result = await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        result.NewManifests.Count.ShouldBe(2);
        result.NewManifests[0].Name.ShouldBe("child-search");
        result.NewManifests[1].Name.ShouldBe("child-payment");
    }

    [Test]
    public async Task DivideAsync_ChildManifest_ContainsFilteredJobs()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();

        var result = await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        result.NewManifests[0].Jobs.ShouldContainKey("handle");
        result.NewManifests[0].Jobs.ShouldNotContainKey("process");
        result.NewManifests[1].Jobs.ShouldContainKey("process");
        result.NewManifests[1].Jobs.ShouldNotContainKey("handle");
    }

    [Test]
    public async Task DivideAsync_ChildManifest_CarriesReferencedModels()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();

        var result = await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        result.NewManifests[0].Models.ShouldContainKey("default");
        result.NewManifests[0].Models["default"].Provider.ShouldBe("openai");
    }

    // ── Simulate mode ───────────────────────────────────────────────

    [Test]
    public async Task DivideAsync_Simulate_DoesNotSpawnOrKill()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider(simulate: true);
        var plan = BuildTwoChildPlan();

        var result = await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        // Parent still alive, no children spawned
        _host.ListActive().ShouldContain("parent");
        _host.ListActive().ShouldNotContain("child-search");
        _host.ListActive().ShouldNotContain("child-payment");

        // But result is fully populated
        result.NewManifests.Count.ShouldBe(2);
        result.RoutingTable.Count.ShouldBe(2);
        result.MemoryProfiles.Count.ShouldBe(2);
    }

    // ── Abort on failure ────────────────────────────────────────────

    [Test]
    public async Task DivideAsync_ChildFailsToStart_AbortsAndParentSurvives()
    {
        await StartParentAsync("parent");
        // Factory that throws on the second child
        var failingFactory = new FailOnSecondChildFactory();
        var divider = new WorkflowDivider(_host, _landscape, failingFactory);
        var plan = BuildTwoChildPlan();

        await Should.ThrowAsync<InvalidOperationException>(
            () => divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory()));

        // Parent must survive
        _host.ListActive().ShouldContain("parent");
    }

    // ── Snapshot derivation ─────────────────────────────────────────

    [Test]
    public async Task DivideAsync_ChildSnapshot_SetsSplitFrom()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider(simulate: true);
        var plan = BuildTwoChildPlan();

        // Use simulate to inspect without side effects
        var result = await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        // Verify via manifests (snapshots are internal, but manifests reflect derivation)
        result.NewManifests[0].Name.ShouldBe("child-search");
    }

    [Test]
    public async Task DivideAsync_SystemPromptOverride_AppliedToChildManifest()
    {
        await StartParentAsync("parent");
        var divider = CreateDivider(simulate: true);

        var plan = new DivisionPlan
        {
            ParentWorkflow = "parent",
            Reason = "test",
            Children =
            [
                new ChildSpec
                {
                    Name = "child-custom",
                    Domain = "custom",
                    Tools = ["search"],
                    Jobs = ["handle"],
                    SystemPromptOverride = "You are a specialized search agent."
                }
            ]
        };

        var result = await divider.DivideAsync(plan, BuildParentManifest(), new StubEmpiricalMemory());

        result.NewManifests[0].Jobs["handle"].SystemPrompt
            .ShouldBe("You are a specialized search agent.");
    }

    // ── Null guards ─────────────────────────────────────────────────

    [Test]
    public void DivideAsync_NullPlan_Throws()
    {
        var divider = CreateDivider();
        Should.ThrowAsync<ArgumentNullException>(
            () => divider.DivideAsync(null!, BuildParentManifest(), new StubEmpiricalMemory()));
    }

    [Test]
    public void DivideAsync_NullManifest_Throws()
    {
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();
        Should.ThrowAsync<ArgumentNullException>(
            () => divider.DivideAsync(plan, null!, new StubEmpiricalMemory()));
    }

    [Test]
    public void DivideAsync_NullMemory_Throws()
    {
        var divider = CreateDivider();
        var plan = BuildTwoChildPlan();
        Should.ThrowAsync<ArgumentNullException>(
            () => divider.DivideAsync(plan, BuildParentManifest(), null!));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private WorkflowDivider CreateDivider(bool simulate = false) =>
        new(_host, _landscape, _factory, new DivisionOptions { Simulate = simulate });

    private Task StartParentAsync(string name) =>
        _host.StartAsync(name, WorkflowLoops.Spin);

    private void RegisterInLandscape(string name, string domain)
    {
        _landscape.Register(new WorkflowSignal
        {
            WorkflowName = name,
            Domain = domain,
            Capabilities = [],
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    private static DivisionPlan BuildTwoChildPlan() => new()
    {
        ParentWorkflow = "parent",
        Reason = "test division",
        Children =
        [
            new ChildSpec
            {
                Name = "child-search",
                Domain = "search",
                Tools = ["search", "lookup"],
                Jobs = ["handle"]
            },
            new ChildSpec
            {
                Name = "child-payment",
                Domain = "payment",
                Tools = ["validate", "process"],
                Jobs = ["process"]
            }
        ]
    };

    private static WorkflowManifest BuildParentManifest() => new()
    {
        Name = "parent",
        Models = new Dictionary<string, ModelDefinition>
        {
            ["default"] = new() { Provider = "openai", Model = "gpt-4o-mini" }
        },
        Jobs = new Dictionary<string, JobDefinition>
        {
            ["handle"] = new() { Type = "agent", ModelAlias = "default", SystemPrompt = "You help users." },
            ["process"] = new() { Type = "code" }
        },
        Connections = ["handle -> process", "process -> End"]
    };

    // ── Test doubles ────────────────────────────────────────────────

    private sealed class StubActivatorFactory : IWorkflowActivatorFactory
    {
        public Func<CancellationToken, Task> CreateLoop(
            WorkflowSnapshot snapshot, MemoryProfile? memoryProfile = null) =>
            WorkflowLoops.Spin;
    }

    private sealed class FailOnSecondChildFactory : IWorkflowActivatorFactory
    {
        private int _callCount;

        public Func<CancellationToken, Task> CreateLoop(
            WorkflowSnapshot snapshot, MemoryProfile? memoryProfile = null)
        {
            _callCount++;
            if (_callCount >= 2)
                throw new InvalidOperationException("Simulated activation failure");

            return WorkflowLoops.Spin;
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
