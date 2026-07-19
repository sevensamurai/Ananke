using Ananke.Abstractions.Agents;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Organics.Division;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class DivisionOutcomeTrackerTests
{
    private TrackingMemory _memory = null!;
    private DivisionOutcomeTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _memory = new TrackingMemory();
        _tracker = new DivisionOutcomeTracker(_memory);
    }

    private static ComplexitySnapshot MakeSnapshot(
        string workflowName = "parent",
        float entropy = 0.8f,
        float contextUtil = 0.5f,
        float avgLatency = 2000f) => new()
        {
            WorkflowName = workflowName,
            ToolCount = 10,
            JobCount = 3,
            TagClusterCount = 3,
            RoutingEntropy = entropy,
            ResourceSpan = 3,
            ContextUtilization = contextUtil,
            AvgLatencyMs = avgLatency,
            MeasuredAt = DateTimeOffset.UtcNow
        };

    private static DivisionPlan MakePlan(params string[] influencingEntries) => new()
    {
        ParentWorkflow = "parent",
        Children =
        [
            new ChildSpec { Name = "child-a", Domain = "a", Tools = ["t1"], Jobs = ["j1"] },
            new ChildSpec { Name = "child-b", Domain = "b", Tools = ["t2"], Jobs = ["j1"] }
        ],
        Reason = "test division",
        InfluencingEntries = influencingEntries
    };

    [Test]
    public async Task RecordBaseline_ThenReward_ReinforcesInfluencingEntries()
    {
        _tracker.RecordBaseline("div-1", MakeSnapshot(entropy: 0.8f, contextUtil: 0.5f, avgLatency: 2000f));

        // Children are better (lower entropy, lower context util, lower latency)
        var children = new List<ComplexitySnapshot>
        {
            MakeSnapshot("child-a", entropy: 0.2f, contextUtil: 0.15f, avgLatency: 800f),
            MakeSnapshot("child-b", entropy: 0.3f, contextUtil: 0.20f, avgLatency: 1000f)
        };

        await _tracker.RewardAsync("div-1", children, MakePlan("entry-1", "entry-2"));

        _memory.ReinforcedIds.ShouldContain("entry-1");
        _memory.ReinforcedIds.ShouldContain("entry-2");
        _memory.ContradictedIds.ShouldBeEmpty();
    }

    [Test]
    public async Task RewardAsync_Improvement_PositiveReward()
    {
        _tracker.RecordBaseline("div-2", MakeSnapshot(entropy: 0.9f, contextUtil: 0.6f, avgLatency: 3000f));

        var children = new List<ComplexitySnapshot>
        {
            MakeSnapshot("child-a", entropy: 0.1f, contextUtil: 0.1f, avgLatency: 500f)
        };

        await _tracker.RewardAsync("div-2", children, MakePlan("entry-x"));

        _memory.ReinforcedIds.ShouldContain("entry-x");
        _memory.LastReinforcement.ShouldNotBeNull();
        // Normalized reward should be > 0.5 (i.e. positive raw reward)
        _memory.LastReinforcement!.Reward.ShouldNotBeNull();
        _memory.LastReinforcement.Reward!.Value.ShouldBeGreaterThan(0.5f);
    }

    [Test]
    public async Task RewardAsync_Regression_ContradictEntries()
    {
        _tracker.RecordBaseline("div-3", MakeSnapshot(entropy: 0.2f, contextUtil: 0.1f, avgLatency: 500f));

        // Children are WORSE (higher entropy, higher context util, higher latency)
        var children = new List<ComplexitySnapshot>
        {
            MakeSnapshot("child-a", entropy: 0.9f, contextUtil: 0.7f, avgLatency: 4000f)
        };

        await _tracker.RewardAsync("div-3", children, MakePlan("bad-entry"));

        _memory.ContradictedIds.ShouldContain("bad-entry");
    }

    [Test]
    public void RewardAsync_UnknownDivisionId_Throws()
    {
        Should.ThrowAsync<InvalidOperationException>(() =>
            _tracker.RewardAsync("unknown", [], MakePlan("e1")));
    }

    [Test]
    public async Task RewardAsync_EmptyInfluencingEntries_NoOp()
    {
        _tracker.RecordBaseline("div-4", MakeSnapshot());

        var children = new List<ComplexitySnapshot> { MakeSnapshot("child") };

        await _tracker.RewardAsync("div-4", children, MakePlan());

        _memory.ReinforcedIds.ShouldBeEmpty();
        _memory.ContradictedIds.ShouldBeEmpty();
    }

    [Test]
    public void ComputeReward_ChildrenBetter_PositiveReward()
    {
        var parent = MakeSnapshot(entropy: 0.8f, contextUtil: 0.5f, avgLatency: 2000f);
        var children = new List<ComplexitySnapshot>
        {
            MakeSnapshot("a", entropy: 0.2f, contextUtil: 0.1f, avgLatency: 800f)
        };

        var reward = DivisionOutcomeTracker.ComputeReward(parent, children);
        reward.ShouldBeGreaterThan(0f);
    }

    [Test]
    public void ComputeReward_ChildrenWorse_NegativeReward()
    {
        var parent = MakeSnapshot(entropy: 0.2f, contextUtil: 0.1f, avgLatency: 500f);
        var children = new List<ComplexitySnapshot>
        {
            MakeSnapshot("a", entropy: 0.9f, contextUtil: 0.7f, avgLatency: 3000f)
        };

        var reward = DivisionOutcomeTracker.ComputeReward(parent, children);
        reward.ShouldBeLessThan(0f);
    }

    [Test]
    public void ComputeReward_NoChildren_ReturnsZero()
    {
        var parent = MakeSnapshot();
        DivisionOutcomeTracker.ComputeReward(parent, []).ShouldBe(0f);
    }

    // ── Tracking fake ───────────────────────────────────────────────

    private sealed class TrackingMemory : IEmpiricalMemory
    {
        public List<string> ReinforcedIds { get; } = [];
        public List<string> ContradictedIds { get; } = [];
        public Reinforcement? LastReinforcement { get; private set; }

        public Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default) =>
            Task.FromResult(entry);

        public Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
            string situation, RecallOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmpiricalMatch>>([]);

        public Task ReinforceAsync(string entryId, Reinforcement reinforcement, CancellationToken ct = default)
        {
            ReinforcedIds.Add(entryId);
            LastReinforcement = reinforcement;
            return Task.CompletedTask;
        }

        public Task ContradictAsync(string entryId, string reason, CancellationToken ct = default)
        {
            ContradictedIds.Add(entryId);
            return Task.CompletedTask;
        }

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
}
