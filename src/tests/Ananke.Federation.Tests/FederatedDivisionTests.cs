using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Division;
using Ananke.Federation.Monitoring;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class FederatedDivisionPolicyTests
{
    [Test]
    public async Task EvaluateAsync_InnerReturnsNull_ReturnsNull()
    {
        var inner = new StubPolicy(null);
        var policy = new FederatedDivisionPolicy(inner, new Dictionary<string, DeploymentProfile>());

        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        result.ShouldBeNull();
    }

    [Test]
    public async Task EvaluateAsync_NoProfiles_ChildrenStayLocal()
    {
        var plan = MakePlan("parent", ["search", "code"]);
        var inner = new StubPolicy(plan);
        var policy = new FederatedDivisionPolicy(inner, new Dictionary<string, DeploymentProfile>());

        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        result.ShouldNotBeNull();
        result.Children.ShouldAllBe(c => c.TargetPlatform == null);
    }

    [Test]
    public async Task EvaluateAsync_ProfileMatchesTools_SetsTargetPlatform()
    {
        var plan = MakePlan("parent", ["search", "code"], ["db_query"]);
        var inner = new StubPolicy(plan);

        var profiles = new Dictionary<string, DeploymentProfile>
        {
            ["azure-ai"] = new()
            {
                Name = "azure-ai",
                Tools = new Dictionary<string, ToolBinding>
                {
                    ["search"] = new() { Execute = "platform", Platform = "bing_search" },
                    ["code"] = new() { Execute = "platform", Platform = "code_interpreter" }
                }
            }
        };

        var policy = new FederatedDivisionPolicy(inner, profiles);
        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        result.ShouldNotBeNull();
        // Child with search+code tools should target azure-ai
        result.Children[0].TargetPlatform.ShouldBe("azure-ai");
        // Child with db_query (no profile match) stays local
        result.Children[1].TargetPlatform.ShouldBeNull();
    }

    [Test]
    public async Task EvaluateAsync_StrugglingPlatform_KeepsChildLocal()
    {
        var plan = MakePlan("parent", ["search"]);
        var inner = new StubPolicy(plan);

        var profiles = new Dictionary<string, DeploymentProfile>
        {
            ["azure-ai"] = new()
            {
                Name = "azure-ai",
                Tools = new Dictionary<string, ToolBinding>
                {
                    ["search"] = new() { Execute = "platform", Platform = "bing_search" }
                }
            }
        };

        // Create tracker with a struggling trend for an azure deployment
        var tracker = new RemoteMetricsTracker(windowSize: 10, minSamplesForTrend: 3);
        for (var i = 1; i <= 5; i++)
        {
            tracker.Record(new RemoteCellMetrics
            {
                DeploymentId = "dep-azure-ai-1",
                ExecutionCount = i * 10,
                TotalTokens = i * 10 * (200 + i * 100), // increasing
                ToolCallCount = i * 10 * (2 + i), // increasing
                ErrorRate = 0,
                MeasuredAt = DateTimeOffset.UtcNow
            });
        }

        var policy = new FederatedDivisionPolicy(inner, profiles, tracker);
        var result = await policy.EvaluateAsync(MakeSnapshot(), MakeManifest());

        result.ShouldNotBeNull();
        // Platform is struggling — keep local
        result.Children[0].TargetPlatform.ShouldBeNull();

        tracker.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ComplexitySnapshot MakeSnapshot() => new()
    {
        WorkflowName = "test",
        ToolCount = 8,
        JobCount = 2,
        TagClusterCount = 2,
        RoutingEntropy = 0.7f,
        ResourceSpan = 4,
        ContextUtilization = 0.5f,
        MeasuredAt = DateTimeOffset.UtcNow
    };

    private static WorkflowManifest MakeManifest() => new()
    {
        Name = "test",
        Models = [],
        Jobs = new Dictionary<string, JobDefinition>
        {
            ["agent"] = new() { Type = "agent" }
        },
        Connections = []
    };

    private static DivisionPlan MakePlan(string parent, params IReadOnlyList<string>[] childToolSets)
    {
        var children = childToolSets.Select((tools, i) => new ChildSpec
        {
            Name = $"{parent}-{(char)('a' + i)}",
            Domain = $"domain-{i}",
            Tools = tools,
            Jobs = [$"job-{i}"]
        }).ToList();

        return new DivisionPlan
        {
            ParentWorkflow = parent,
            Children = children,
            Reason = "Test division"
        };
    }

    private sealed class StubPolicy(DivisionPlan? result) : IDivisionPolicy
    {
        public Task<DivisionPlan?> EvaluateAsync(
            ComplexitySnapshot snapshot, WorkflowManifest manifest, CancellationToken ct = default)
            => Task.FromResult(result);
    }
}

[TestFixture]
public sealed class PlatformDivisionApprovalGateTests
{
    [Test]
    public async Task LocalOnly_DelegatesToInnerGate()
    {
        var gate = new PlatformDivisionApprovalGate(new AutoApprovalGate());

        var plan = new DivisionPlan
        {
            ParentWorkflow = "test",
            Children = [new ChildSpec { Name = "a", Domain = "d", Tools = ["t1"], Jobs = ["j1"], TargetPlatform = null }],
            Reason = "test"
        };

        var result = await gate.ReviewAsync(plan, MakeSnapshot());
        result.IsApproved.ShouldBeTrue();
    }

    [Test]
    public async Task PlatformTargeted_NoCallback_Rejects()
    {
        var gate = new PlatformDivisionApprovalGate(new AutoApprovalGate());

        var plan = new DivisionPlan
        {
            ParentWorkflow = "test",
            Children = [new ChildSpec { Name = "a", Domain = "d", Tools = ["t1"], Jobs = ["j1"], TargetPlatform = "azure-ai" }],
            Reason = "test"
        };

        var result = await gate.ReviewAsync(plan, MakeSnapshot());
        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldContain("azure-ai");
    }

    [Test]
    public async Task PlatformTargeted_WithCallback_DelegatesToCallback()
    {
        var callbackInvoked = false;
        var gate = new PlatformDivisionApprovalGate(
            new AutoApprovalGate(),
            humanCallback: (_, _, _) =>
            {
                callbackInvoked = true;
                return Task.FromResult(DivisionApproval.Approve("manual", "operator"));
            });

        var plan = new DivisionPlan
        {
            ParentWorkflow = "test",
            Children = [new ChildSpec { Name = "a", Domain = "d", Tools = ["t1"], Jobs = ["j1"], TargetPlatform = "vertex-ai" }],
            Reason = "test"
        };

        var result = await gate.ReviewAsync(plan, MakeSnapshot());
        callbackInvoked.ShouldBeTrue();
        result.IsApproved.ShouldBeTrue();
    }

    [Test]
    public async Task MixedTargets_TreatedAsPlatformDivision()
    {
        var gate = new PlatformDivisionApprovalGate(new AutoApprovalGate());

        var plan = new DivisionPlan
        {
            ParentWorkflow = "test",
            Children =
            [
                new ChildSpec { Name = "a", Domain = "d1", Tools = ["t1"], Jobs = ["j1"], TargetPlatform = null },
                new ChildSpec { Name = "b", Domain = "d2", Tools = ["t2"], Jobs = ["j2"], TargetPlatform = "claude" }
            ],
            Reason = "test"
        };

        var result = await gate.ReviewAsync(plan, MakeSnapshot());
        result.IsApproved.ShouldBeFalse(); // Any remote target = needs human approval
    }

    private static ComplexitySnapshot MakeSnapshot() => new()
    {
        WorkflowName = "test",
        ToolCount = 6,
        JobCount = 2,
        TagClusterCount = 2,
        RoutingEntropy = 0.5f,
        ResourceSpan = 3,
        ContextUtilization = 0.3f,
        MeasuredAt = DateTimeOffset.UtcNow
    };
}
