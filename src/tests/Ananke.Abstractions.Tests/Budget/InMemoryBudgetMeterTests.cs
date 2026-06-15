using Ananke.Abstractions.Budget;
using Shouldly;

namespace Ananke.Abstractions.Tests.Budget;

[TestFixture]
public class InMemoryBudgetMeterTests
{
    // ── BudgetSpend record ────────────────────────────────────────────

    [Test]
    public void BudgetSpend_Properties_RoundTrip()
    {
        var spend = new BudgetSpend { TokensIn = 100, TokensOut = 200, EstimatedUsd = 0.05m };

        spend.TokensIn.ShouldBe(100L);
        spend.TokensOut.ShouldBe(200L);
        spend.EstimatedUsd.ShouldBe(0.05m);
    }

    [Test]
    public void BudgetSpend_Equality_WorksByValue()
    {
        var a = new BudgetSpend { TokensIn = 10, TokensOut = 20, EstimatedUsd = 1m };
        var b = new BudgetSpend { TokensIn = 10, TokensOut = 20, EstimatedUsd = 1m };

        a.ShouldBe(b);
    }

    // ── GetCurrentSpend: no records ───────────────────────────────────

    [Test]
    public void GetCurrentSpend_NoRecords_ReturnsZeroSpend()
    {
        var meter = new InMemoryBudgetMeter();

        var spend = meter.GetCurrentSpend("unknown-role");

        spend.TokensIn.ShouldBe(0L);
        spend.TokensOut.ShouldBe(0L);
        spend.EstimatedUsd.ShouldBe(0m);
    }

    // ── Record + GetCurrentSpend ──────────────────────────────────────

    [Test]
    public void GetCurrentSpend_AfterSingleRecord_ReturnsItExactly()
    {
        var meter = new InMemoryBudgetMeter();

        meter.Record("r1", 100, 50, 0.01m);

        var spend = meter.GetCurrentSpend("r1");
        spend.TokensIn.ShouldBe(100L);
        spend.TokensOut.ShouldBe(50L);
        spend.EstimatedUsd.ShouldBe(0.01m);
    }

    [Test]
    public void GetCurrentSpend_AfterMultipleRecords_SumsAll()
    {
        var meter = new InMemoryBudgetMeter();

        meter.Record("r1", 100, 50, 0.01m);
        meter.Record("r1", 200, 100, 0.02m);

        var spend = meter.GetCurrentSpend("r1");
        spend.TokensIn.ShouldBe(300L);
        spend.TokensOut.ShouldBe(150L);
        spend.EstimatedUsd.ShouldBe(0.03m);
    }

    [Test]
    public void GetCurrentSpend_DifferentRoles_AreIndependent()
    {
        var meter = new InMemoryBudgetMeter();

        meter.Record("role-a", 100, 50, 0.01m);
        meter.Record("role-b", 300, 150, 0.05m);

        meter.GetCurrentSpend("role-a").TokensIn.ShouldBe(100L);
        meter.GetCurrentSpend("role-b").TokensIn.ShouldBe(300L);
    }

    // ── IsOverCap ─────────────────────────────────────────────────────

    [Test]
    public void IsOverCap_TotalBelowCap_ReturnsFalse()
    {
        var meter = new InMemoryBudgetMeter();
        meter.Record("r", 100, 50, 0.01m);

        meter.IsOverCap("r", 1000).ShouldBeFalse();
    }

    [Test]
    public void IsOverCap_TotalAtOrAboveCap_ReturnsTrue()
    {
        var meter = new InMemoryBudgetMeter();
        meter.Record("r", 100, 50, 0.01m);

        meter.IsOverCap("r", 150).ShouldBeTrue();
    }

    [Test]
    public void IsOverCap_ZeroCap_ReturnsTrueWhenAnyTokensRecorded()
    {
        var meter = new InMemoryBudgetMeter();
        meter.Record("r", 1, 0, 0m);

        meter.IsOverCap("r", 0).ShouldBeTrue();
    }

    [Test]
    public void IsOverCap_NoRecords_ReturnsFalse()
    {
        var meter = new InMemoryBudgetMeter();

        meter.IsOverCap("r", 1000).ShouldBeFalse();
    }

    // ── Time-window pruning ───────────────────────────────────────────

    [Test]
    public void GetCurrentSpend_RecordsOutsideWindow_AreExcluded()
    {
        var past = DateTimeOffset.UtcNow.AddHours(-2);
        var fakeClock = new FakeTimeProvider(past);
        var meter = new InMemoryBudgetMeter(timeWindow: TimeSpan.FromHours(1), clock: fakeClock);

        meter.Record("r", 500, 500, 1.00m);

        fakeClock.AdvanceTo(DateTimeOffset.UtcNow);

        var spend = meter.GetCurrentSpend("r");
        spend.TokensIn.ShouldBe(0L);
        spend.TokensOut.ShouldBe(0L);
    }

    [Test]
    public void GetCurrentSpend_RecentRecords_AreIncluded()
    {
        var now = DateTimeOffset.UtcNow;
        var fakeClock = new FakeTimeProvider(now);
        var meter = new InMemoryBudgetMeter(timeWindow: TimeSpan.FromHours(1), clock: fakeClock);

        meter.Record("r", 100, 50, 0.01m);

        meter.GetCurrentSpend("r").TokensIn.ShouldBe(100L);
    }

    [Test]
    public void GetCurrentSpend_MixedWindowRecords_OnlyIncludesRecent()
    {
        var fakeClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-90));
        var meter = new InMemoryBudgetMeter(timeWindow: TimeSpan.FromHours(1), clock: fakeClock);

        meter.Record("r", 999, 999, 9.99m);

        fakeClock.AdvanceTo(DateTimeOffset.UtcNow.AddMinutes(-30));
        meter.Record("r", 100, 50, 0.01m);

        fakeClock.AdvanceTo(DateTimeOffset.UtcNow);

        var spend = meter.GetCurrentSpend("r");
        spend.TokensIn.ShouldBe(100L);
    }

    // ── Validation ────────────────────────────────────────────────────

    [Test]
    public void Record_EmptyRole_Throws()
        => Should.Throw<ArgumentException>(() => new InMemoryBudgetMeter().Record("", 1, 1, 1m));

    [Test]
    public void Record_WhiteSpaceRole_Throws()
        => Should.Throw<ArgumentException>(() => new InMemoryBudgetMeter().Record("  ", 1, 1, 1m));

    [Test]
    public void Record_NegativeTokensIn_Throws()
        => Should.Throw<ArgumentOutOfRangeException>(() => new InMemoryBudgetMeter().Record("r", -1, 0, 0m));

    [Test]
    public void Record_NegativeTokensOut_Throws()
        => Should.Throw<ArgumentOutOfRangeException>(() => new InMemoryBudgetMeter().Record("r", 0, -1, 0m));

    [Test]
    public void Record_NegativeUsd_Throws()
        => Should.Throw<ArgumentOutOfRangeException>(() => new InMemoryBudgetMeter().Record("r", 0, 0, -0.01m));

    [Test]
    public void GetCurrentSpend_EmptyRole_Throws()
        => Should.Throw<ArgumentException>(() => new InMemoryBudgetMeter().GetCurrentSpend(""));

    [Test]
    public void IsOverCap_NegativeCap_Throws()
        => Should.Throw<ArgumentOutOfRangeException>(() => new InMemoryBudgetMeter().IsOverCap("r", -1));

    // ── Role key case-insensitivity ───────────────────────────────────

    [Test]
    public void Record_RoleKeyIsCaseInsensitive()
    {
        var meter = new InMemoryBudgetMeter();

        meter.Record("MyRole", 100, 50, 0.01m);

        meter.GetCurrentSpend("myrole").TokensIn.ShouldBe(100L);
        meter.GetCurrentSpend("MYROLE").TokensIn.ShouldBe(100L);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public void AdvanceTo(DateTimeOffset time) => _now = time;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
