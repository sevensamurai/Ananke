using Ananke.Organics.Division.Review;
using Shouldly;

namespace Ananke.Organics.Tests.Division.Review;

[TestFixture]
public sealed class ParkingCallbackWorkReviewGateTests
{
    private static WorkItem MakeItem(string id = "item-1") => new()
    {
        Id = id,
        Title = "Review this",
        Kind = WorkItemKind.Wireframe,
        Payload = "payload"
    };

    private static ParkingCallbackWorkReviewGate MakeGate(
        IWorkReviewParkingStore? store = null, string gateId = "gate-1") =>
        new(store ?? new InMemoryWorkReviewParkingStore(), gateId);

    // --- Park → returns Pending ---

    [Test]
    public async Task ReviewAsync_ReturnsPendingOutcome()
    {
        var gate = MakeGate();

        var decision = await gate.ReviewAsync(MakeItem());

        decision.Outcome.ShouldBe(WorkReviewOutcome.Pending);
        decision.ReviewerId.ShouldBe("system");
    }

    [Test]
    public async Task ReviewAsync_PendingComment_IsNonEmptyParkingId()
    {
        var gate = MakeGate();

        var decision = await gate.ReviewAsync(MakeItem());

        decision.Comment.ShouldNotBeNullOrWhiteSpace();
    }

    // --- Park → Resume happy path ---

    [Test]
    public async Task ResumeAsync_KnownParkingId_CompletesStore()
    {
        var store = new InMemoryWorkReviewParkingStore();
        var gate = new ParkingCallbackWorkReviewGate(store, "g1");

        var pending = await gate.ReviewAsync(MakeItem());
        var parkingId = pending.Comment;

        // Verify parked
        (await store.TryGetAsync(parkingId)).ShouldNotBeNull();

        await gate.ResumeAsync(parkingId, WorkReviewDecision.Approve("ok", "reviewer-1"));

        // Entry should be removed from store after resume
        (await store.TryGetAsync(parkingId)).ShouldBeNull();
    }

    [Test]
    public async Task ResumeAsync_UnknownParkingId_Throws()
    {
        var gate = MakeGate();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => gate.ResumeAsync("no-such-id", WorkReviewDecision.Approve("ok", "r")));
    }

    // --- Parking id collision ---

    [Test]
    public async Task TwoParallelParks_HaveDistinctParkingIds()
    {
        var gate = MakeGate();

        var d1 = await gate.ReviewAsync(MakeItem("a"));
        var d2 = await gate.ReviewAsync(MakeItem("b"));

        d1.Comment.ShouldNotBe(d2.Comment);
    }

    // --- Cancellation ---

    [Test]
    public async Task ReviewAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var gate = MakeGate();

        await Should.ThrowAsync<OperationCanceledException>(
            () => gate.ReviewAsync(MakeItem(), cts.Token));
    }

    [Test]
    public async Task GateId_ReflectsConstructorArgument()
    {
        var gate = MakeGate(gateId: "my-gate");

        gate.GateId.ShouldBe("my-gate");
    }
}
