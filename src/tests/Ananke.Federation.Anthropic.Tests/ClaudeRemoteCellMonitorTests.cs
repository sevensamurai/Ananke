using Ananke.Federation.Anthropic;
using Shouldly;

namespace Ananke.Federation.Anthropic.Tests;

[TestFixture]
public sealed class ClaudeRemoteCellMonitorTests
{
    private ClaudeRemoteCellMonitor _monitor = null!;

    [SetUp]
    public void SetUp() => _monitor = new ClaudeRemoteCellMonitor();

    [Test]
    public void Platform_is_claude()
    {
        _monitor.Platform.ShouldBe("claude");
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
        metrics.ErrorRate.ShouldBe(0);
    }
}
