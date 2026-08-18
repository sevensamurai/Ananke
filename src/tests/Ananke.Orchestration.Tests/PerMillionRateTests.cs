using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Budget;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Rates are declared in the unit providers publish — per million tokens. Ananke ships no rate
/// table, so this is only about making the user's own figures easy to state correctly.
/// </summary>
[TestFixture]
public class PerMillionRateTests
{
    private static TokenUsage Tokens(int input, int output) =>
        new() { InputTokens = input, OutputTokens = output };

    [Test]
    public void PerMillion_ConvertsExactly_NoFloatingPointDrift()
    {
        var rates = ModelCostRates.PerMillion(input: 0.15m, output: 0.60m);

        rates.CostPer1KInputTokens.ShouldBe(0.00015m);
        rates.CostPer1KOutputTokens.ShouldBe(0.0006m);
    }

    [Test]
    public void PerMillion_OneMillionTokens_CostsExactlyTheQuotedRate()
    {
        var rates = ModelCostRates.PerMillion(input: 0.15m, output: 0.60m);

        rates.EstimateCost(Tokens(1_000_000, 0)).ShouldBe(0.15m);
        rates.EstimateCost(Tokens(0, 1_000_000)).ShouldBe(0.60m);
        rates.EstimateCost(Tokens(1_000_000, 1_000_000)).ShouldBe(0.75m);
    }

    /// <summary>
    /// The mistake this exists to prevent: pasting a published per-million figure into the
    /// per-1K constructor makes the budget 1000x too loose, and nothing says so until the bill.
    /// </summary>
    [Test]
    public void PerMillion_IsAThousandthOfTheSameNumberPer1K()
    {
        var correct = ModelCostRates.PerMillion(input: 0.15m, output: 0.60m);
        var misread = new ModelCostRates(0.15m, 0.60m);

        var usage = Tokens(1_000_000, 1_000_000);
        (misread.EstimateCost(usage) / correct.EstimateCost(usage)).ShouldBe(1000m);
    }

    [Test]
    public void PerMillion_CapturesThatOutputCostsMoreThanInput()
    {
        // Mid-tier pricing: output is 4x input. A single token ceiling cannot express this,
        // which is why rates are declared as a pair.
        var rates = ModelCostRates.PerMillion(input: 0.15m, output: 0.60m);

        rates.EstimateCost(Tokens(0, 1_000_000))
            .ShouldBe(rates.EstimateCost(Tokens(1_000_000, 0)) * 4m);
    }

    [Test]
    public void BudgetConfig_FromPerMillion_MatchesTheRatesForm()
    {
        var budget = BudgetConfig.FromPerMillion(maxCost: 25m, inputPerMillion: 0.15m, outputPerMillion: 0.60m);
        var rates = ModelCostRates.PerMillion(0.15m, 0.60m);

        budget.MaxCost.ShouldBe(25m);
        budget.EstimateCost(Tokens(1_000_000, 500_000))
            .ShouldBe(rates.EstimateCost(Tokens(1_000_000, 500_000)));
    }

    [Test]
    public void BudgetConfig_FromPerMillion_CountsAsAConfiguredRateSource()
    {
        BudgetConfig.FromPerMillion(25m, 0.15m, 0.60m).HasFallbackRates.ShouldBeTrue(
            "a workflow declaring published rates must not be told it has no rate source");
    }

    [Test]
    public void PerMillion_ZeroRates_AreStillZero()
    {
        // Local or self-hosted models: declaring zero must stay zero, not become a tiny number.
        ModelCostRates.PerMillion(0m, 0m).EstimateCost(Tokens(5_000_000, 5_000_000)).ShouldBe(0m);
    }
}
