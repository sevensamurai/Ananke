using Ananke.Federation.Azure;
using Shouldly;

namespace Ananke.Federation.Azure.Tests;

[TestFixture]
public sealed class AzureRemoteCellMonitorTests
{
    private AzureRemoteCellMonitor _monitor = null!;

    [SetUp]
    public void SetUp() => _monitor = new AzureRemoteCellMonitor();

    [Test]
    public void Platform_is_azure_ai()
    {
        _monitor.Platform.ShouldBe("azure-ai");
    }

    [Test]
    public async Task GetHealth_returns_healthy_baseline()
    {
        var health = await _monitor.GetHealthAsync("dep-1");

        health.DeploymentId.ShouldBe("dep-1");
        health.IsHealthy.ShouldBeTrue();
        health.ErrorCount.ShouldBe(0);
    }

    [Test]
    public async Task GetMetrics_returns_zero_baseline()
    {
        var metrics = await _monitor.GetMetricsAsync("dep-1");

        metrics.DeploymentId.ShouldBe("dep-1");
        metrics.ExecutionCount.ShouldBe(0);
        metrics.TotalTokens.ShouldBe(0);
        metrics.ErrorRate.ShouldBe(0);
    }
}
