using System.Text.Json;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Abstractions.Tests.Distributed;

[TestFixture]
public class InMemoryDistributedLockTests
{
    private InMemoryDistributedLock _store = new();

    [SetUp]
    public void SetUp() => _store = new InMemoryDistributedLock();

    [TearDown]
    public ValueTask TearDown() => _store.DisposeAsync();

    [Test]
    public async Task GetValueAsync_MissingKey_ReturnsNull()
    {
        (await _store.GetValueAsync("missing")).ShouldBeNull();
        (await _store.GetValueAsync<int[]>("missing")).ShouldBeNull();
    }

    [Test]
    public async Task GetValueAsyncOfT_ValueNotJson_Throws()
    {
        await _store.SetValueAsync("k", "not json");

        await Should.ThrowAsync<JsonException>(() => _store.GetValueAsync<int[]>("k"));
    }

    [Test]
    public async Task SetValueAsync_Expiry_KeyGoneAfterIt()
    {
        var clock = new FakeTimeProvider();
        await using var store = new InMemoryDistributedLock(clock);

        await store.SetValueAsync("k", "v", TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromSeconds(59));
        (await store.GetValueAsync("k")).ShouldBe("v");
        clock.Advance(TimeSpan.FromSeconds(1));

        (await store.GetValueAsync("k")).ShouldBeNull();
        (await store.GetValueAsync<string>("k")).ShouldBeNull();
        (await store.ExistsAsync("k")).ShouldBeFalse();
        (await store.RemoveAsync("k")).ShouldBeFalse();
    }

    [Test]
    public async Task SetValueAsync_WithoutExpiry_ReplacesEarlierExpiry()
    {
        var clock = new FakeTimeProvider();
        await using var store = new InMemoryDistributedLock(clock);

        await store.SetValueAsync("k", 1, TimeSpan.FromMinutes(1));
        await store.SetValueAsync("k", 2);
        clock.Advance(TimeSpan.FromDays(1));

        (await store.GetValueAsync<int>("k")).ShouldBe(2);
    }

    [Test]
    public async Task SetValueAsync_ExpiredKeyNeverRead_RemovedByLaterWrite()
    {
        var clock = new FakeTimeProvider();
        await using var store = new InMemoryDistributedLock(clock);

        await store.SetValueAsync("short", "v", TimeSpan.FromMinutes(1));
        await store.SetValueAsync("long", "v", TimeSpan.FromMinutes(10));
        await store.SetValueAsync("kept", "v");
        clock.Advance(TimeSpan.FromMinutes(1));
        await store.SetValueAsync("other", "v");

        store.Count.ShouldBe(3);
        (await store.GetValueAsync("long")).ShouldBe("v");

        clock.Advance(TimeSpan.FromMinutes(9));
        await store.SetValueAsync("other", "w");

        store.Count.ShouldBe(2);
        (await store.GetValueAsync("kept")).ShouldBe("v");
    }

    [Test]
    public async Task SetValueAsync_KeyRewrittenWithoutExpiry_KeptPastOldExpiry()
    {
        var clock = new FakeTimeProvider();
        await using var store = new InMemoryDistributedLock(clock);

        await store.SetValueAsync("k", "old", TimeSpan.FromMinutes(1));
        await store.SetValueAsync("k", "new");
        clock.Advance(TimeSpan.FromMinutes(1));
        await store.SetValueAsync("other", "v");

        store.Count.ShouldBe(2);
        (await store.GetValueAsync("k")).ShouldBe("new");
    }

    [Test]
    public async Task SetValueAsync_ManyKeysExpiring_AllRemovedByLaterWrite()
    {
        var clock = new FakeTimeProvider();
        await using var store = new InMemoryDistributedLock(clock);

        for (var i = 0; i < 200; i++)
            await store.SetValueAsync($"k{i % 50}", "v", TimeSpan.FromSeconds(1 + i));
        clock.Advance(TimeSpan.FromSeconds(200));
        await store.SetValueAsync("other", "v");

        store.Count.ShouldBe(1);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task SetValueAsync_ExpiryNotPositive_Throws(int seconds)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            _store.SetValueAsync("k", "v", TimeSpan.FromSeconds(seconds)));

        (await _store.ExistsAsync("k")).ShouldBeFalse();
    }
}
