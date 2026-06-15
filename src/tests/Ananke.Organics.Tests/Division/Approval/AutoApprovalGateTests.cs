using Ananke.Abstractions.Agents;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class AutoApprovalGateTests
{
    private static DivisionPlan CreatePlan() => new()
    {
        ParentWorkflow = "test-cell",
        Children =
        [
            new ChildSpec { Name = "test-a", Domain = "a", Tools = ["t1"], Jobs = ["j1"] },
            new ChildSpec { Name = "test-b", Domain = "b", Tools = ["t2"], Jobs = ["j2"] }
        ],
        Reason = "surface tension"
    };

    private static ComplexitySnapshot CreateSnapshot() => new()
    {
        WorkflowName = "test-cell",
        ToolCount = 8,
        JobCount = 4,
        TagClusterCount = 3,
        RoutingEntropy = 0.7f,
        ResourceSpan = 2,
        ContextUtilization = 0.4f,
        MeasuredAt = DateTimeOffset.UtcNow
    };

    [Test]
    public async Task ReviewAsync_AlwaysApproves()
    {
        var gate = new AutoApprovalGate();

        var result = await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeTrue();
        result.RevisedPlan.ShouldBeNull();
        result.ReviewedBy.ShouldBe("auto");
    }

    [Test]
    public void ReviewAsync_NullPlan_Throws()
    {
        var gate = new AutoApprovalGate();

        Should.ThrowAsync<ArgumentNullException>(
            () => gate.ReviewAsync(null!, CreateSnapshot()));
    }
}
