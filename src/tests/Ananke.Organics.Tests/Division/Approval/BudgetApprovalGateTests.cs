using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class BudgetApprovalGateTests
{
    private static DivisionPlan CreatePlan() => new()
    {
        ParentWorkflow = "reviewer",
        Children = [new ChildSpec { Name = "child", Domain = "review", Tools = ["lint"], Jobs = ["review"] }],
        Reason = "too much work"
    };

    private static ComplexitySnapshot CreateSnapshot() => new()
    {
        WorkflowName = "reviewer",
        ToolCount = 1,
        JobCount = 1,
        TagClusterCount = 1,
        RoutingEntropy = 0.1f,
        ResourceSpan = 1,
        ContextUtilization = 0.1f,
        MeasuredAt = DateTimeOffset.UtcNow
    };

    [Test]
    public async Task ReviewAsync_UnderCap_Approves()
    {
        var meter = new InMemoryBudgetMeter();
        meter.Record("reviewer", 25, 25, 0.01m);
        var gate = new BudgetApprovalGate(meter, tokenCap: 100);

        var result = await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeTrue();
        result.ReviewedBy.ShouldBe("budget-meter");
    }

    [Test]
    public async Task ReviewAsync_AtCap_Rejects()
    {
        var meter = new InMemoryBudgetMeter();
        meter.Record("reviewer", 40, 60, 0.02m);
        var gate = new BudgetApprovalGate(meter, tokenCap: 100);

        var result = await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldContain("meeting or exceeding cap 100");
    }
}
