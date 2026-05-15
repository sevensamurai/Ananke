using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Exploration;
using Ananke.Organics.Division;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class ExperienceDrivenDivisionPolicyTests
{
    private static WorkflowManifest MakeManifest() => new()
    {
        Name = "test-cell",
        Models = new Dictionary<string, ModelDefinition>
        {
            ["default"] = new() { Provider = "openai", Model = "gpt-4o-mini" }
        },
        Jobs = new Dictionary<string, JobDefinition>
        {
            ["handle-request"] = new() { Type = "agent", ModelAlias = "default" },
            ["respond"] = new() { Type = "code" }
        },
        Connections = ["handle-request -> respond"]
    };

    private static ComplexitySnapshot MakeSnapshot(
        int toolCount = 10, int tagClusters = 3) => new()
    {
        WorkflowName = "test-cell",
        ToolCount = toolCount,
        JobCount = 3,
        TagClusterCount = tagClusters,
        RoutingEntropy = 0.8f,
        ResourceSpan = 3,
        ContextUtilization = 0.45f,
        MeasuredAt = DateTimeOffset.UtcNow
    };

    private static EmpiricalEntry MakeDivisionEntry(
        string id, float valence = 0.7f, float strength = 0.8f,
        float variance = 0.3f, int observations = 5) => new()
    {
        Id = id,
        Kind = EmpiricalKind.Heuristic,
        Tags = ["division"],
        Source = "division-outcome-tracker",
        Description = SemanticDescription.FromText("Domain-cluster splits work well"),
        Confidence = 0.8f,
        Valence = valence,
        Strength = strength,
        Variance = variance,
        ObservationCount = observations,
        Evidence = ["div-outcome:positive"],
        FirstObserved = DateTimeOffset.UtcNow.AddDays(-7),
        LastObserved = DateTimeOffset.UtcNow.AddHours(-1),
        Situation = "high tool count with distinct tag clusters",
        PreferredApproach = "split on domain-tool clusters"
    };

    // ── Cold start ──────────────────────────────────────────────────

    [Test]
    public async Task EvaluateAsync_NoDivisionMemory_DelegatesToFallback()
    {
        var emptyMemory = new StubMemory(recallResults: []);
        var fallback = new StubPolicy(shouldDivide: true);
        var strategy = new AlwaysSelectFirstStrategy();

        var policy = new ExperienceDrivenDivisionPolicy(
            emptyMemory, strategy, fallback);

        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        // Should delegate to fallback, which returns a plan
        result.ShouldNotBeNull();
        fallback.WasCalled.ShouldBeTrue();
    }

    [Test]
    public async Task EvaluateAsync_NoDivisionMemory_FallbackRejectsReturnsNull()
    {
        var emptyMemory = new StubMemory(recallResults: []);
        var fallback = new StubPolicy(shouldDivide: false);
        var strategy = new AlwaysSelectFirstStrategy();

        var policy = new ExperienceDrivenDivisionPolicy(
            emptyMemory, strategy, fallback);

        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        result.ShouldBeNull();
    }

    // ── Warm start ──────────────────────────────────────────────────

    [Test]
    public async Task EvaluateAsync_WithPositiveMemory_UsesDivisionStrategy()
    {
        var divisionEntry = MakeDivisionEntry("entry-1", valence: 0.8f, strength: 0.9f);
        var memoryWithEntries = new StubMemory(recallResults:
        [
            new EmpiricalMatch { Entry = divisionEntry, Score = 0.85f }
        ]);

        // Strategy that selects index 1 (first recalled entry = "divide")
        var strategy = new AlwaysSelectIndexStrategy(1);
        var fallback = new StubPolicy(shouldDivide: true);
        IReadOnlyList<ChildSpec> Cluster(string parent, WorkflowManifest _) =>
        [
            new ChildSpec { Name = $"{parent}-a", Domain = "a", Tools = ["t1", "t2"], Jobs = ["handle-request", "respond"] },
            new ChildSpec { Name = $"{parent}-b", Domain = "b", Tools = ["t3", "t4"], Jobs = ["handle-request", "respond"] }
        ];

        var policy = new ExperienceDrivenDivisionPolicy(
            memoryWithEntries, strategy, fallback, clusterStrategy: Cluster);

        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        result.ShouldNotBeNull();
        result.Children.Count.ShouldBe(2);
        fallback.WasCalled.ShouldBeFalse(); // Did NOT fall back
    }

    [Test]
    public async Task EvaluateAsync_WithNegativeMemory_MayRejectDivision()
    {
        var negativeEntry = MakeDivisionEntry("entry-neg", valence: -0.8f, strength: 0.3f);
        var memoryWithEntries = new StubMemory(recallResults:
        [
            new EmpiricalMatch { Entry = negativeEntry, Score = 0.5f }
        ]);

        // Strategy selects index 0 = "do not divide"
        var strategy = new AlwaysSelectFirstStrategy();
        var fallback = new StubPolicy(shouldDivide: true);

        var policy = new ExperienceDrivenDivisionPolicy(
            memoryWithEntries, strategy, fallback);

        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        result.ShouldBeNull(); // Exploration chose "do not divide"
    }

    [Test]
    public async Task EvaluateAsync_PopulatesInfluencingEntries()
    {
        var entry1 = MakeDivisionEntry("inf-1");
        var entry2 = MakeDivisionEntry("inf-2");
        var memoryWithEntries = new StubMemory(recallResults:
        [
            new EmpiricalMatch { Entry = entry1, Score = 0.9f },
            new EmpiricalMatch { Entry = entry2, Score = 0.8f }
        ]);

        var strategy = new AlwaysSelectIndexStrategy(1); // "divide"
        var fallback = new StubPolicy(shouldDivide: true);
        IReadOnlyList<ChildSpec> Cluster(string parent, WorkflowManifest _) =>
        [
            new ChildSpec { Name = $"{parent}-a", Domain = "a", Tools = ["t1"], Jobs = ["j1"] },
            new ChildSpec { Name = $"{parent}-b", Domain = "b", Tools = ["t2"], Jobs = ["j1"] }
        ];

        var policy = new ExperienceDrivenDivisionPolicy(
            memoryWithEntries, strategy, fallback, clusterStrategy: Cluster);

        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        result.ShouldNotBeNull();
        result.InfluencingEntries.ShouldContain("inf-1");
        result.InfluencingEntries.ShouldContain("inf-2");
    }

    [Test]
    public async Task EvaluateAsync_UsesExplorationStrategy()
    {
        var entry = MakeDivisionEntry("e1");
        var memoryWithEntries = new StubMemory(recallResults:
        [
            new EmpiricalMatch { Entry = entry, Score = 0.9f }
        ]);

        var trackingStrategy = new TrackingStrategy();
        var fallback = new StubPolicy(shouldDivide: false);

        var policy = new ExperienceDrivenDivisionPolicy(
            memoryWithEntries, trackingStrategy, fallback);

        await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        trackingStrategy.WasCalled.ShouldBeTrue();
        // Should have 2 candidates: "do not divide" + 1 recalled entry
        trackingStrategy.CandidateCount.ShouldBe(2);
    }

    // ── Stubs ───────────────────────────────────────────────────────

    private sealed class StubMemory(IReadOnlyList<EmpiricalMatch> recallResults) : IEmpiricalMemory
    {
        public Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default) =>
            Task.FromResult(entry);

        public Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
            string situation, RecallOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult(recallResults);

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

        public Task MarkConsolidatedAsync(string entryId, string knowledgeDocId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<EmpiricalMatch>> PairRecallAsync(
            EmpiricalEntry reference, PairRecallOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmpiricalMatch>>([]);
    }

    private sealed class StubPolicy(bool shouldDivide) : IDivisionPolicy
    {
        public bool WasCalled { get; private set; }

        public Task<DivisionPlan?> EvaluateAsync(
            ComplexitySnapshot snapshot, WorkflowManifest manifest, CancellationToken ct = default)
        {
            WasCalled = true;
            if (!shouldDivide)
                return Task.FromResult<DivisionPlan?>(null);

            return Task.FromResult<DivisionPlan?>(new DivisionPlan
            {
                ParentWorkflow = snapshot.WorkflowName,
                Children =
                [
                    new ChildSpec { Name = $"{snapshot.WorkflowName}-a", Domain = "a", Tools = ["t1", "t2"], Jobs = ["handle-request", "respond"] },
                    new ChildSpec { Name = $"{snapshot.WorkflowName}-b", Domain = "b", Tools = ["t3", "t4"], Jobs = ["handle-request", "respond"] }
                ],
                Reason = "Fallback threshold policy"
            });
        }
    }

    /// <summary>Always selects index 0 (= "do not divide").</summary>
    private sealed class AlwaysSelectFirstStrategy : IExplorationStrategy
    {
        public int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections) => 0;
    }

    /// <summary>Always selects the specified index.</summary>
    private sealed class AlwaysSelectIndexStrategy(int index) : IExplorationStrategy
    {
        public int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections) =>
            index < actions.Count ? index : 0;
    }

    /// <summary>Tracks whether SelectAction was called and with how many candidates.</summary>
    private sealed class TrackingStrategy : IExplorationStrategy
    {
        public bool WasCalled { get; private set; }
        public int CandidateCount { get; private set; }

        public int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections)
        {
            WasCalled = true;
            CandidateCount = actions.Count;
            return 0; // "do not divide"
        }
    }
}
