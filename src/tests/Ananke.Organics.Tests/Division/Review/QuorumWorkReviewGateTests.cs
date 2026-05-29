using Ananke.Organics.Division.Review;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class QuorumWorkReviewGateTests
{
    private static WorkItem CreateWorkItem() => new()
    {
        Id = "design-1",
        Title = "Review the design",
        Kind = WorkItemKind.DesignDoc,
        Payload = "Design payload"
    };

    [Test]
    public async Task ReviewAsync_AllOf_AllApprove_ReturnsApproved()
    {
        var gate = new QuorumWorkReviewGate(
            [
                new FixedReviewGate(WorkReviewDecision.Approve("ok", "alice")),
                new FixedReviewGate(WorkReviewDecision.Approve("ok", "bob"))
            ],
            WorkReviewQuorum.RequireAllOf("alice", "bob"));

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Approved);
        result.ReviewerId.ShouldBe("quorum");
    }

    [Test]
    public async Task ReviewAsync_AllOf_Rejection_ReturnsRejected()
    {
        var gate = new QuorumWorkReviewGate(
            [
                new FixedReviewGate(WorkReviewDecision.Approve("ok", "alice")),
                new FixedReviewGate(WorkReviewDecision.Reject("blocked", "bob"))
            ],
            WorkReviewQuorum.RequireAllOf("alice", "bob"));

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Rejected);
        result.ReviewerId.ShouldBe("bob");
    }

    [Test]
    public async Task ReviewAsync_AnyOf_OneApproves_ReturnsApproved()
    {
        var gate = new QuorumWorkReviewGate(
            [
                new FixedReviewGate(WorkReviewDecision.Reject("no", "alice")),
                new FixedReviewGate(WorkReviewDecision.Approve("yes", "bob"))
            ],
            WorkReviewQuorum.RequireAllOf("bob").AndAnyOf("alice", "bob"));

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Approved);
    }

    [Test]
    public async Task ReviewAsync_AnyOf_NoneApprove_ReturnsRejected()
    {
        var gate = new QuorumWorkReviewGate(
            [
                new FixedReviewGate(WorkReviewDecision.Reject("no", "alice")),
                new FixedReviewGate(WorkReviewDecision.Reject("still no", "bob"))
            ],
            WorkReviewQuorum.RequireAllOf("alice").AndAnyOf("alice", "bob"));

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Rejected);
    }

    [Test]
    public async Task ReviewAsync_MixedQuorum_WithRevision_ReturnsRevised()
    {
        var gate = new QuorumWorkReviewGate(
            [
                new FixedReviewGate(WorkReviewDecision.Approve("core ok", "alice")),
                new FixedReviewGate(WorkReviewDecision.Revise("tighten wording", "carol"))
            ],
            WorkReviewQuorum.RequireAllOf("alice").AndAnyOf("carol"));

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Revised);
        result.Comment.ShouldContain("tighten wording");
    }

    private sealed class FixedReviewGate(WorkReviewDecision decision) : IWorkReviewGate
    {
        public Task<WorkReviewDecision> ReviewAsync(WorkItem item, CancellationToken ct = default) =>
            Task.FromResult(decision);
    }
}
