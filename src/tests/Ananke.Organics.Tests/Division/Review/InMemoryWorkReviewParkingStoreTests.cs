using Ananke.Organics.Division.Review;
using Shouldly;

namespace Ananke.Organics.Tests.Division.Review;

[TestFixture]
public sealed class InMemoryWorkReviewParkingStoreTests
{
    private static WorkItem MakeItem(string id = "item-1") => new()
    {
        Id = id,
        Title = "Review this",
        Kind = WorkItemKind.Wireframe,
        Payload = "payload"
    };

    [Test]
    public async Task ParkAsync_ReturnsNonEmptyId()
    {
        var store = new InMemoryWorkReviewParkingStore();

        var id = await store.ParkAsync(MakeItem(), "gate-1");

        id.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task ParkAsync_TwoItems_ReturnDistinctIds()
    {
        var store = new InMemoryWorkReviewParkingStore();

        var id1 = await store.ParkAsync(MakeItem("a"), "gate-1");
        var id2 = await store.ParkAsync(MakeItem("b"), "gate-1");

        id1.ShouldNotBe(id2);
    }

    [Test]
    public async Task TryGetAsync_ExistingId_ReturnsItemAndGateId()
    {
        var store = new InMemoryWorkReviewParkingStore();
        var item = MakeItem();
        var parkingId = await store.ParkAsync(item, "gate-42");

        var result = await store.TryGetAsync(parkingId);

        result.ShouldNotBeNull();
        result!.Value.Item.Id.ShouldBe(item.Id);
        result.Value.GateId.ShouldBe("gate-42");
    }

    [Test]
    public async Task TryGetAsync_UnknownId_ReturnsNull()
    {
        var store = new InMemoryWorkReviewParkingStore();

        var result = await store.TryGetAsync("no-such-id");

        result.ShouldBeNull();
    }

    [Test]
    public async Task CompleteAsync_RemovesEntry()
    {
        var store = new InMemoryWorkReviewParkingStore();
        var parkingId = await store.ParkAsync(MakeItem(), "gate-1");

        await store.CompleteAsync(parkingId);

        var result = await store.TryGetAsync(parkingId);
        result.ShouldBeNull();
    }

    [Test]
    public async Task CompleteAsync_UnknownId_IsNoOp()
    {
        var store = new InMemoryWorkReviewParkingStore();

        await Should.NotThrowAsync(() => store.CompleteAsync("no-such-id"));
    }
}
