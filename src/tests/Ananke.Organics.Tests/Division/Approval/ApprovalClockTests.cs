using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Budget;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Sensing;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class ApprovalClockTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static DivisionPlan CreatePlan() => new()
    {
        ParentWorkflow = "cell",
        Children = [new ChildSpec { Name = "child", Domain = "a", Tools = ["t1"], Jobs = ["j1"] }],
        Reason = "test"
    };

    private static ComplexitySnapshot CreateSnapshot(double errorRate = 0) => new()
    {
        WorkflowName = "cell",
        ToolCount = 1,
        JobCount = 1,
        TagClusterCount = 1,
        RoutingEntropy = 0.1f,
        ResourceSpan = 1,
        ContextUtilization = 0.1f,
        MeasuredAt = Now,
        ErrorRate = errorRate
    };

    [Test]
    public void Factories_StampReviewedAtFromTheirClock()
    {
        var clock = new FakeTimeProvider(Now);

        DivisionApproval.Approve("ok", clock: clock).ReviewedAt.ShouldBe(Now);
        DivisionApproval.Reject("no", clock: clock).ReviewedAt.ShouldBe(Now);
        DivisionApproval.Revise(CreatePlan(), "changed", clock: clock).ReviewedAt.ShouldBe(Now);
    }

    [Test]
    public async Task AutoApprovalGate_StampsReviewedAtFromItsClock()
    {
        var result = await new AutoApprovalGate(new FakeTimeProvider(Now)).ReviewAsync(CreatePlan(), CreateSnapshot());

        result.ReviewedAt.ShouldBe(Now);
    }

    [TestCase(10, true)]
    [TestCase(1_000, false)]
    public async Task BudgetApprovalGate_StampsReviewedAtFromItsClock(long tokenCap, bool rejects)
    {
        var meter = new InMemoryBudgetMeter();
        meter.Record("cell", 50, 50, 0.01m);

        var result = await new BudgetApprovalGate(meter, tokenCap, new FakeTimeProvider(Now))
            .ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBe(!rejects);
        result.ReviewedAt.ShouldBe(Now);
    }

    [TestCase("APPROVED: fine")]
    [TestCase("REJECTED: coupled")]
    [TestCase("no verdict")]
    public async Task LlmApprovalGate_StampsReviewedAtFromItsClock(string reply)
    {
        var result = await new LlmApprovalGate(new FixedReply(reply), clock: new FakeTimeProvider(Now))
            .ReviewAsync(CreatePlan(), CreateSnapshot());

        result.ReviewedAt.ShouldBe(Now);
    }

    [Test]
    public async Task QuorumApprovalGate_Rejection_StampsReviewedAtFromItsClock()
    {
        var aggregator = new InMemoryMeshAggregator();
        aggregator.Report("cell", MetabolicSignal.Stressed);

        var result = await new QuorumApprovalGate(new AutoApprovalGate(), aggregator, clock: new FakeTimeProvider(Now))
            .ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeFalse();
        result.ReviewedAt.ShouldBe(Now);
    }

    [TestCase(0.15)]
    [TestCase(0.35)]
    public async Task MetabolicDivisionApprovalGate_Rejection_StampsReviewedAtFromItsClock(double errorRate)
    {
        var result = await new MetabolicDivisionApprovalGate(new AutoApprovalGate(), clock: new FakeTimeProvider(Now))
            .ReviewAsync(CreatePlan(), CreateSnapshot(errorRate));

        result.IsApproved.ShouldBeFalse();
        result.ReviewedAt.ShouldBe(Now);
    }

    private sealed class FixedReply(string text) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = text });
    }
}
