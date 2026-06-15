using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Organics.Division;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class ThresholdDivisionPolicyTests
{
    private static ComplexitySnapshot MakeSnapshot(
        string workflowName = "test-cell",
        int toolCount = 8,
        int tagClusterCount = 2) => new()
    {
        WorkflowName = workflowName,
        ToolCount = toolCount,
        JobCount = 4,
        TagClusterCount = tagClusterCount,
        RoutingEntropy = 0.8f,
        ResourceSpan = 3,
        ContextUtilization = 0.4f,
        MeasuredAt = DateTimeOffset.UtcNow
    };

    private static WorkflowManifest MakeManifest(int jobCount = 4) => new()
    {
        Name = "test-workflow",
        Models = new Dictionary<string, ModelDefinition>
        {
            ["default"] = new() { Provider = "openai", Model = "gpt-4.1-mini" }
        },
        Jobs = Enumerable.Range(0, jobCount)
            .ToDictionary(i => $"job-{i}", _ => new JobDefinition()),
        Connections = []
    };

    // ── Below threshold → null ──────────────────────────────────────

    [Test]
    public async Task EvaluateAsync_BelowToolThreshold_ReturnsNull()
    {
        var policy = new ThresholdDivisionPolicy(minTools: 6, minClusters: 2);
        var snapshot = MakeSnapshot(toolCount: 3, tagClusterCount: 2);

        var result = await policy.EvaluateAsync(snapshot, MakeManifest());

        result.ShouldBeNull();
    }

    [Test]
    public async Task EvaluateAsync_BelowClusterThreshold_ReturnsNull()
    {
        var policy = new ThresholdDivisionPolicy(minTools: 6, minClusters: 2);
        var snapshot = MakeSnapshot(toolCount: 10, tagClusterCount: 1);

        var result = await policy.EvaluateAsync(snapshot, MakeManifest());

        result.ShouldBeNull();
    }

    [Test]
    public async Task EvaluateAsync_HighToolCount_LowClusters_ReturnsNull()
    {
        var policy = new ThresholdDivisionPolicy(minTools: 6, minClusters: 2);
        var snapshot = MakeSnapshot(toolCount: 20, tagClusterCount: 1);

        var result = await policy.EvaluateAsync(snapshot, MakeManifest());

        result.ShouldBeNull();
    }

    // ── Above threshold → plan ──────────────────────────────────────

    [Test]
    public async Task EvaluateAsync_AboveThreshold_ReturnsPlan()
    {
        var policy = new ThresholdDivisionPolicy(minTools: 6, minClusters: 2);
        var snapshot = MakeSnapshot(toolCount: 8, tagClusterCount: 3);

        var result = await policy.EvaluateAsync(snapshot, MakeManifest(jobCount: 4));

        result.ShouldNotBeNull();
        result.ParentWorkflow.ShouldBe("test-cell");
        result.Children.Count.ShouldBeGreaterThanOrEqualTo(2);
        result.Reason.ShouldContain("Surface tension");
    }

    [Test]
    public async Task DefaultSplit_AllJobsAssigned()
    {
        var policy = new ThresholdDivisionPolicy(minTools: 6, minClusters: 2);
        var manifest = MakeManifest(jobCount: 4);
        var snapshot = MakeSnapshot(toolCount: 8, tagClusterCount: 2);

        var result = await policy.EvaluateAsync(snapshot, manifest);

        result.ShouldNotBeNull();
        var allJobs = result.Children.SelectMany(c => c.Jobs).ToList();
        allJobs.Count.ShouldBe(4);
        allJobs.Distinct().Count().ShouldBe(4);
    }

    [Test]
    public async Task DefaultSplit_ChildrenHaveDisjointJobs()
    {
        var policy = new ThresholdDivisionPolicy(minTools: 6, minClusters: 2);
        var manifest = MakeManifest(jobCount: 6);
        var snapshot = MakeSnapshot(toolCount: 8, tagClusterCount: 2);

        var result = await policy.EvaluateAsync(snapshot, manifest);

        result.ShouldNotBeNull();
        var jobSets = result.Children.Select(c => c.Jobs.ToHashSet()).ToList();
        jobSets[0].Overlaps(jobSets[1]).ShouldBeFalse();
    }

    // ── Custom cluster strategy ─────────────────────────────────────

    [Test]
    public async Task EvaluateAsync_UsesCustomClusterStrategy()
    {
        var customChildren = new List<ChildSpec>
        {
            new() { Name = "custom-a", Domain = "browse", Tools = ["search"], Jobs = ["agent-a"] },
            new() { Name = "custom-b", Domain = "payment", Tools = ["pay"], Jobs = ["agent-b"] }
        };

        var policy = new ThresholdDivisionPolicy(
            minTools: 6, minClusters: 2,
            clusterStrategy: (_, _) => customChildren);

        var snapshot = MakeSnapshot(toolCount: 8, tagClusterCount: 2);
        var result = await policy.EvaluateAsync(snapshot, MakeManifest());

        result.ShouldNotBeNull();
        result.Children.ShouldBe(customChildren);
    }

    // ── Single-job manifest → no split ──────────────────────────────

    [Test]
    public async Task EvaluateAsync_SingleJobManifest_ReturnsNull()
    {
        var policy = new ThresholdDivisionPolicy(minTools: 6, minClusters: 2);
        var snapshot = MakeSnapshot(toolCount: 8, tagClusterCount: 2);
        var manifest = MakeManifest(jobCount: 1);

        var result = await policy.EvaluateAsync(snapshot, manifest);

        result.ShouldBeNull();
    }
}
