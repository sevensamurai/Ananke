using Ananke.Abstractions.Agents;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Division;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel.Snapshots;

[TestFixture]
public class WorkflowActivatorFactoryTests
{
    private ToolKit _tools = null!;
    private FakeModel _fakeModel = null!;

    [SetUp]
    public void SetUp()
    {
        _tools = new ToolKit("test-tools")
            .AddTool("search", "Searches", () => ToolResult.Ok("result"))
            .AddTool("lookup", "Looks up", (q) => ToolResult.Ok($"found: {q}"), "query", "Query");

        _fakeModel = new FakeModel("Hello from agent");
    }

    // ── CreateLoop basics ───────────────────────────────────────────

    [Test]
    public void CreateLoop_CodeOnlyCell_ReturnsNonNullLoop()
    {
        var snapshot = BuildCodeOnlySnapshot();
        var factory = BuildCodeFactory();

        var loop = factory.CreateLoop(snapshot);

        loop.ShouldNotBeNull();
    }

    [Test]
    public async Task CreateLoop_CodeOnlyCell_LoopRunsAndCancels()
    {
        var snapshot = BuildCodeOnlySnapshot();
        var processed = false;
        var factory = new TypedWorkflowActivatorFactory<TestState>()
            .WithInitialStateFactory(() => new TestState())
            .WithCodeJobHandler((state, _) =>
            {
                processed = true;
                return Task.FromResult(state with { Output = "done" });
            });

        var loop = factory.CreateLoop(snapshot);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await loop(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected — loop runs until cancelled
        }

        processed.ShouldBeTrue();
    }

    [Test]
    public void CreateLoop_NullSnapshot_Throws()
    {
        var factory = BuildCodeFactory();

        Should.Throw<ArgumentNullException>(() => factory.CreateLoop(null!));
    }

    [Test]
    public void CreateLoop_NoInitialStateFactory_Throws()
    {
        var snapshot = BuildCodeOnlySnapshot();
        var factory = new TypedWorkflowActivatorFactory<TestState>();

        Should.Throw<InvalidOperationException>(() => factory.CreateLoop(snapshot))
            .Message.ShouldContain("initial state factory");
    }

    // ── Memory profile ──────────────────────────────────────────────

    [Test]
    public void CreateLoop_WithMemoryProfile_ReturnsLoop()
    {
        var snapshot = BuildCodeOnlySnapshot();
        var memory = new StubEmpiricalMemory();
        var profile = new MemoryProfile
        {
            Domains = ["search", "general"],
            LineageTags = ["bookstore"]
        };

        var factory = BuildCodeFactory()
            .WithSharedMemory(memory);

        var loop = factory.CreateLoop(snapshot, profile);
        loop.ShouldNotBeNull();
    }

    [Test]
    public void CreateLoop_WithMemoryProfile_WithoutSharedMemory_ReturnsLoop()
    {
        // MemoryProfile without shared memory → no domain-affine wrapping, no error
        var snapshot = BuildCodeOnlySnapshot();
        var profile = new MemoryProfile
        {
            Domains = ["search"],
            LineageTags = []
        };

        var factory = BuildCodeFactory();

        var loop = factory.CreateLoop(snapshot, profile);
        loop.ShouldNotBeNull();
    }

    [Test]
    public void CreateLoop_WithoutMemoryProfile_ReturnsLoop()
    {
        var snapshot = BuildCodeOnlySnapshot();
        var memory = new StubEmpiricalMemory();

        var factory = BuildCodeFactory()
            .WithSharedMemory(memory);

        // No memory profile → no domain-affine wrapping
        var loop = factory.CreateLoop(snapshot);
        loop.ShouldNotBeNull();
    }

    // ── Fluent API ──────────────────────────────────────────────────

    [Test]
    public void FluentApi_AllMethodsReturnSameInstance()
    {
        var factory = new TypedWorkflowActivatorFactory<TestState>();

        var result = factory
            .WithTools(_tools)
            .WithModelFactory(_ => _fakeModel)
            .WithPromptBuilder((s, _) => s.Input)
            .WithResultMapper((s, _, text) => s with { Output = text })
            .WithCodeJobHandler((s, _) => Task.FromResult(s))
            .WithInitialStateFactory(() => new TestState())
            .WithSharedMemory(new StubEmpiricalMemory());

        result.ShouldBeSameAs(factory);
    }

    [Test]
    public void WithTools_Null_Throws()
    {
        var factory = new TypedWorkflowActivatorFactory<TestState>();
        Should.Throw<ArgumentNullException>(() => factory.WithTools(null!));
    }

    [Test]
    public void WithModelFactory_Null_Throws()
    {
        var factory = new TypedWorkflowActivatorFactory<TestState>();
        Should.Throw<ArgumentNullException>(() => factory.WithModelFactory(null!));
    }

    [Test]
    public void WithInitialStateFactory_Null_Throws()
    {
        var factory = new TypedWorkflowActivatorFactory<TestState>();
        Should.Throw<ArgumentNullException>(() => factory.WithInitialStateFactory(null!));
    }

    [Test]
    public void WithOrganicHost_Null_Throws()
    {
        var factory = new TypedWorkflowActivatorFactory<TestState>();
        Should.Throw<ArgumentNullException>(() => factory.WithOrganicHost(null!));
    }

    [Test]
    public void WithSharedMemory_Null_Throws()
    {
        var factory = new TypedWorkflowActivatorFactory<TestState>();
        Should.Throw<ArgumentNullException>(() => factory.WithSharedMemory(null!));
    }

    // ── IWorkflowActivatorFactory interface ─────────────────────────

    [Test]
    public void ImplementsInterface()
    {
        var factory = new TypedWorkflowActivatorFactory<TestState>();
        factory.ShouldBeAssignableTo<IWorkflowActivatorFactory>();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private TypedWorkflowActivatorFactory<TestState> BuildCodeFactory() =>
        new TypedWorkflowActivatorFactory<TestState>()
            .WithInitialStateFactory(() => new TestState());

    private static WorkflowSnapshot BuildCodeOnlySnapshot() => new()
    {
        Name = "test-cell",
        Domain = "test",
        Tools = [],
        Connections = ["process -> End"],
        Jobs = new Dictionary<string, JobSnapshot>
        {
            ["process"] = new() { Type = "code" }
        },
        Models = new Dictionary<string, ModelSnapshot>()
    };

    private sealed record TestState
    {
        public string Input { get; init; } = "test input";
        public string? Output { get; init; }
    }

    private sealed class FakeModel(string response) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = response });
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

        public int Count => 0;
    }
}
