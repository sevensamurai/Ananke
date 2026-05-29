using Ananke.Organics.Division.Review;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class CallbackWorkReviewGateTests
{
    private static WorkItem CreateWorkItem() => new()
    {
        Id = "wireframe-1",
        Title = "Review the wireframe",
        Kind = WorkItemKind.Wireframe,
        Payload = "Wireframe payload"
    };

    [Test]
    public async Task ReviewAsync_DelegatesToCallback()
    {
        WorkItem? captured = null;
        var gate = new CallbackWorkReviewGate((item, _) =>
        {
            captured = item;
            return Task.FromResult(WorkReviewDecision.Approve("approved", "user-123"));
        });

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Approved);
        result.ReviewerId.ShouldBe("user-123");
        captured.ShouldNotBeNull();
        captured!.Id.ShouldBe("wireframe-1");
    }

    [Test]
    public void ReviewAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var gate = new CallbackWorkReviewGate((_, ct) =>
            Task.FromCanceled<WorkReviewDecision>(ct));

        Should.ThrowAsync<OperationCanceledException>(
            () => gate.ReviewAsync(CreateWorkItem(), cts.Token));
    }
}
