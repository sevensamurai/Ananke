using Microsoft.Extensions.Time.Testing;
using Ananke.Organics.Division.Approval;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class InMemoryBudgetMeterTests
{
    [Test]
    public void GetCurrentSpend_WindowRollover_DropsExpiredSamples()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var meter = new InMemoryBudgetMeter(TimeSpan.FromMinutes(5), clock);

        meter.Record("drafter", 10, 20, 0.01m);
        clock.Advance(TimeSpan.FromMinutes(6));
        meter.Record("drafter", 5, 5, 0.02m);

        var spend = meter.GetCurrentSpend("drafter");

        spend.TokensIn.ShouldBe(5);
        spend.TokensOut.ShouldBe(5);
        spend.EstimatedUsd.ShouldBe(0.02m);
    }

    [Test]
    public void Record_ConcurrentIncrements_AreAggregated()
    {
        var meter = new InMemoryBudgetMeter();

        Parallel.For(0, 100, _ => meter.Record("drafter", 1, 2, 0.5m));

        var spend = meter.GetCurrentSpend("drafter");

        spend.TokensIn.ShouldBe(100);
        spend.TokensOut.ShouldBe(200);
        spend.EstimatedUsd.ShouldBe(50m);
    }
}
