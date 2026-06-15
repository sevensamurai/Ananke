using Ananke.Abstractions.Agents;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class DivisionApprovalTests
{
    [Test]
    public void Approve_SetsIsApprovedTrue()
    {
        var result = DivisionApproval.Approve("looks good", "alice");

        result.IsApproved.ShouldBeTrue();
        result.Reason.ShouldBe("looks good");
        result.ReviewedBy.ShouldBe("alice");
        result.RevisedPlan.ShouldBeNull();
    }

    [Test]
    public void Reject_SetsIsApprovedFalse()
    {
        var result = DivisionApproval.Reject("not ready", "bob");

        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldBe("not ready");
        result.ReviewedBy.ShouldBe("bob");
        result.RevisedPlan.ShouldBeNull();
    }

    [Test]
    public void Revise_SetsApprovedWithRevisedPlan()
    {
        var plan = new DivisionPlan
        {
            ParentWorkflow = "cell",
            Children =
            [
                new ChildSpec { Name = "a", Domain = "a", Tools = [], Jobs = ["j1"] }
            ],
            Reason = "revised"
        };

        var result = DivisionApproval.Revise(plan, "adjusted split", "carol");

        result.IsApproved.ShouldBeTrue();
        result.RevisedPlan.ShouldBe(plan);
        result.Reason.ShouldBe("adjusted split");
        result.ReviewedBy.ShouldBe("carol");
    }

    [Test]
    public void ReviewedAt_DefaultsToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var result = DivisionApproval.Approve("ok");
        var after = DateTimeOffset.UtcNow;

        result.ReviewedAt.ShouldBeGreaterThanOrEqualTo(before);
        result.ReviewedAt.ShouldBeLessThanOrEqualTo(after);
    }
}
