using System.Diagnostics.Metrics;
using Ananke.OpenTelemetry.Budget;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.OpenTelemetry.Tests;

[TestFixture]
public sealed class OpenTelemetryBudgetMeterTests
{
    [Test]
    public void GetCurrentSpend_WindowRollover_DropsExpiredSamples()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var budgetMeter = new OpenTelemetryBudgetMeter(
            new BudgetMeterOptions { TimeWindow = TimeSpan.FromMinutes(5) },
            clock);
        using var meter = new Meter(Sources.Federation);
        var tokensIn = meter.CreateCounter<long>("ananke.federation.tokens.in");
        var tokensOut = meter.CreateCounter<long>("ananke.federation.tokens.out");
        var usd = meter.CreateCounter<double>("ananke.federation.usd");

        Publish(tokensIn, tokensOut, usd, "drafter", 10, 20, 0.01);
        clock.Advance(TimeSpan.FromMinutes(6));
        Publish(tokensIn, tokensOut, usd, "drafter", 5, 5, 0.02);

        var spend = budgetMeter.GetCurrentSpend("drafter");

        spend.TokensIn.ShouldBe(5);
        spend.TokensOut.ShouldBe(5);
        spend.EstimatedUsd.ShouldBe(0.02m);
    }

    [Test]
    public void IsOverCap_UsesConfiguredDefaultAndPerRoleOverrides()
    {
        using var budgetMeter = new OpenTelemetryBudgetMeter(new BudgetMeterOptions
        {
            DefaultTokenCap = 50,
            PerRoleCaps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["reviewer"] = 30
            }
        });
        using var meter = new Meter(Sources.Federation);
        var tokensIn = meter.CreateCounter<long>("ananke.federation.tokens.in");
        var tokensOut = meter.CreateCounter<long>("ananke.federation.tokens.out");
        var usd = meter.CreateCounter<double>("ananke.federation.usd");

        Publish(tokensIn, tokensOut, usd, "writer", 20, 20, 0.01);
        Publish(tokensIn, tokensOut, usd, "reviewer", 10, 10, 0.01);

        budgetMeter.GetConfiguredCap("writer").ShouldBe(50);
        budgetMeter.GetConfiguredCap("reviewer").ShouldBe(30);
        budgetMeter.IsOverCap("writer").ShouldBeFalse();
        budgetMeter.IsOverCap("reviewer").ShouldBeFalse();

        Publish(tokensIn, tokensOut, usd, "writer", 10, 5, 0.01);
        Publish(tokensIn, tokensOut, usd, "reviewer", 5, 6, 0.01);

        budgetMeter.IsOverCap("writer").ShouldBeTrue();
        budgetMeter.IsOverCap("reviewer").ShouldBeTrue();
    }

    private static void Publish(
        Counter<long> tokensIn,
        Counter<long> tokensOut,
        Counter<double> usd,
        string workflow,
        long input,
        long output,
        double estimatedUsd)
    {
        KeyValuePair<string, object?>[] tags =
        [
            new("workflow", workflow)
        ];

        tokensIn.Add(input, tags);
        tokensOut.Add(output, tags);
        usd.Add(estimatedUsd, tags);
    }
}
