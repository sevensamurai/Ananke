using Ananke.Orchestration.Knowledge.Catalog;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests;

[TestFixture]
public class TimeDecayTests
{
    [Test]
    public void ComputeWeight_AtIndexTime_ReturnsFullWeight()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0.3f };

        var weight = TimeDecay.ComputeWeight(now, now, options);

        weight.ShouldBe(1.0f, tolerance: 0.0001f);
    }

    [Test]
    public void ComputeWeight_Exponential_AtHalfLife_ReturnsApproximatelyHalf()
    {
        var now = DateTimeOffset.UtcNow;
        var indexedAt = now.AddDays(-90);
        var options = new TimeDecayOptions
        {
            Function = TimeDecayFunction.Exponential,
            HalfLifeDays = 90,
            FloorWeight = 0f
        };

        var weight = TimeDecay.ComputeWeight(indexedAt, now, options);

        weight.ShouldBe(0.5f, tolerance: 0.01f);
    }

    [Test]
    public void ComputeWeight_Exponential_FarInFuture_ApproachesFloorWeightButNeverGoesBelow()
    {
        var now = DateTimeOffset.UtcNow;
        var indexedAt = now.AddYears(-50);
        var options = new TimeDecayOptions
        {
            Function = TimeDecayFunction.Exponential,
            HalfLifeDays = 90,
            FloorWeight = 0.3f
        };

        var weight = TimeDecay.ComputeWeight(indexedAt, now, options);

        weight.ShouldBe(0.3f, tolerance: 0.0001f);
    }

    [Test]
    public void ComputeWeight_Linear_AtHalfLife_ReturnsApproximatelyHalf()
    {
        var now = DateTimeOffset.UtcNow;
        var indexedAt = now.AddDays(-90);
        var options = new TimeDecayOptions
        {
            Function = TimeDecayFunction.Linear,
            HalfLifeDays = 90,
            FloorWeight = 0f
        };

        var weight = TimeDecay.ComputeWeight(indexedAt, now, options);

        weight.ShouldBe(0.5f, tolerance: 0.0001f);
    }

    [Test]
    public void ComputeWeight_Linear_AtTwiceHalfLife_ReachesZeroBeforeFloor()
    {
        var now = DateTimeOffset.UtcNow;
        var indexedAt = now.AddDays(-180); // exactly 2x halfLifeDays
        var options = new TimeDecayOptions
        {
            Function = TimeDecayFunction.Linear,
            HalfLifeDays = 90,
            FloorWeight = 0f
        };

        var weight = TimeDecay.ComputeWeight(indexedAt, now, options);

        weight.ShouldBe(0f, tolerance: 0.0001f);
    }

    [Test]
    public void ComputeWeight_Linear_PastTwiceHalfLife_ClampsToFloorNotNegative()
    {
        var now = DateTimeOffset.UtcNow;
        var indexedAt = now.AddDays(-1000);
        var options = new TimeDecayOptions
        {
            Function = TimeDecayFunction.Linear,
            HalfLifeDays = 90,
            FloorWeight = 0.3f
        };

        var weight = TimeDecay.ComputeWeight(indexedAt, now, options);

        weight.ShouldBe(0.3f, tolerance: 0.0001f);
    }

    [Test]
    public void ComputeWeight_FutureTimestamp_TreatedAsZeroAge()
    {
        // indexedAt after "now" would otherwise produce a negative age; the implementation
        // clamps age to a minimum of 0 rather than producing a weight above 1.0.
        var now = DateTimeOffset.UtcNow;
        var indexedAt = now.AddDays(10);
        var options = new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0f };

        var weight = TimeDecay.ComputeWeight(indexedAt, now, options);

        weight.ShouldBe(1.0f, tolerance: 0.0001f);
    }

    [Test]
    public void ComputeWeight_OverloadWithoutExplicitNow_UsesCurrentTime()
    {
        var options = new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0f };

        var weight = TimeDecay.ComputeWeight(DateTimeOffset.UtcNow, options);

        weight.ShouldBe(1.0f, tolerance: 0.01f);
    }

    [Test]
    public void Apply_MultipliesScoreByComputedWeight()
    {
        var now = DateTimeOffset.UtcNow;
        var indexedAt = now.AddDays(-90);
        var options = new TimeDecayOptions
        {
            Function = TimeDecayFunction.Exponential,
            HalfLifeDays = 90,
            FloorWeight = 0f
        };

        var decayed = TimeDecay.Apply(1.0f, indexedAt, options);

        decayed.ShouldBe(0.5f, tolerance: 0.01f);
    }

    [Test]
    public void Apply_ZeroScore_RemainsZeroRegardlessOfDecay()
    {
        var options = new TimeDecayOptions { HalfLifeDays = 1, FloorWeight = 0f };

        var decayed = TimeDecay.Apply(0f, DateTimeOffset.UtcNow.AddYears(-10), options);

        decayed.ShouldBe(0f);
    }
}
