using Ananke.Abstractions.Agents;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class CallbackApprovalGateTests
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
    public async Task ReviewAsync_DelegatesToCallback()
    {
        DivisionPlan? captured = null;
        var gate = new CallbackApprovalGate((plan, _, _) =>
        {
            captured = plan;
            return Task.FromResult(DivisionApproval.Reject("Human said no", "user-123"));
        });

        var result = await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldBe("Human said no");
        result.ReviewedBy.ShouldBe("user-123");
        captured.ShouldNotBeNull();
        captured!.ParentWorkflow.ShouldBe("test-cell");
    }

    [Test]
    public async Task ReviewAsync_CallbackCanRevise()
    {
        var gate = new CallbackApprovalGate((plan, _, _) =>
        {
            var revised = plan with { Reason = "Revised by human" };
            return Task.FromResult(DivisionApproval.Revise(revised, "Adjusted split", "reviewer"));
        });

        var result = await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeTrue();
        result.RevisedPlan.ShouldNotBeNull();
        result.RevisedPlan!.Reason.ShouldBe("Revised by human");
    }

    [Test]
    public void ReviewAsync_NullPlan_Throws()
    {
        var gate = new CallbackApprovalGate((_, _, _) =>
            Task.FromResult(DivisionApproval.Approve("ok")));

        Should.ThrowAsync<ArgumentNullException>(
            () => gate.ReviewAsync(null!, CreateSnapshot()));
    }

    [Test]
    public void ReviewAsync_NullSnapshot_Throws()
    {
        var gate = new CallbackApprovalGate((_, _, _) =>
            Task.FromResult(DivisionApproval.Approve("ok")));

        Should.ThrowAsync<ArgumentNullException>(
            () => gate.ReviewAsync(CreatePlan(), null!));
    }
}
