using Ananke.Redis;
using Shouldly;

namespace Ananke.Redis.Tests;

/// <summary>
/// Unit tests for <see cref="RedisCheckpointStore"/>'s pure TTL decision logic — the part of the
/// class that is Ananke's own responsibility, as opposed to Redis's behavior. No Redis connection
/// or Docker is needed: <see cref="RedisCheckpointStore.ComputeTtlDecision"/> and
/// <see cref="RedisCheckpointStore.IsExpired"/> are plain functions of two timestamps.
/// </summary>
[TestFixture]
public class RedisCheckpointStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void ComputeTtlDecision_NoExpiry_ReturnsNullTtlAndDoesNotDelete()
    {
        var decision = RedisCheckpointStore.ComputeTtlDecision(DateTimeOffset.MaxValue, Now);

        decision.ShouldDelete.ShouldBeFalse();
        decision.Ttl.ShouldBeNull();
    }

    [Test]
    public void ComputeTtlDecision_FutureExpiry_ReturnsPositiveTtl()
    {
        var expiresAt = Now.AddMinutes(5);

        var decision = RedisCheckpointStore.ComputeTtlDecision(expiresAt, Now);

        decision.ShouldDelete.ShouldBeFalse();
        decision.Ttl.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void ComputeTtlDecision_AlreadyExpired_ReturnsShouldDelete()
    {
        var expiresAt = Now.AddSeconds(-1);

        var decision = RedisCheckpointStore.ComputeTtlDecision(expiresAt, Now);

        decision.ShouldDelete.ShouldBeTrue();
        decision.Ttl.ShouldBeNull();
    }

    [Test]
    public void ComputeTtlDecision_ExpiresExactlyNow_ReturnsShouldDelete()
    {
        // Boundary: a checkpoint expiring at exactly "now" must not be saved with a zero/negative
        // TTL (StackExchange.Redis rejects non-positive expiries) — it should be deleted instead.
        var decision = RedisCheckpointStore.ComputeTtlDecision(Now, Now);

        decision.ShouldDelete.ShouldBeTrue();
    }

    [Test]
    public void IsExpired_FutureExpiry_ReturnsFalse() =>
        RedisCheckpointStore.IsExpired(Now.AddMinutes(1), Now).ShouldBeFalse();

    [Test]
    public void IsExpired_PastExpiry_ReturnsTrue() =>
        RedisCheckpointStore.IsExpired(Now.AddMinutes(-1), Now).ShouldBeTrue();

    [Test]
    public void IsExpired_ExpiresExactlyNow_ReturnsTrue() =>
        RedisCheckpointStore.IsExpired(Now, Now).ShouldBeTrue();
}
