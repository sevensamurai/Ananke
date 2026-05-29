using Ananke.OpenTelemetry.Budget;
using Ananke.Organics.Division.Approval;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ananke.OpenTelemetry.Tests;

[TestFixture]
public sealed class BudgetMeterServiceCollectionExtensionsTests
{
    [Test]
    public void AddBudgetMeter_RegistersBudgetMeterAndOptions()
    {
        var services = new ServiceCollection();

        services.AddBudgetMeter(options =>
        {
            options.DefaultTokenCap = 123;
            options.PerRoleCaps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["reviewer"] = 45
            };
        });

        using var provider = services.BuildServiceProvider();

        var budgetMeter = provider.GetService<IBudgetMeter>();
        var concrete = provider.GetService<OpenTelemetryBudgetMeter>();
        var options = provider.GetService<BudgetMeterOptions>();

        budgetMeter.ShouldNotBeNull();
        concrete.ShouldNotBeNull();
        budgetMeter.ShouldBeSameAs(concrete);
        options.ShouldNotBeNull();
        options.DefaultTokenCap.ShouldBe(123);
        concrete.GetConfiguredCap("writer").ShouldBe(123);
        concrete.GetConfiguredCap("reviewer").ShouldBe(45);
    }
}
