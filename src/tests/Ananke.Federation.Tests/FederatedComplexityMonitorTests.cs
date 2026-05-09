using Ananke.Orchestration.Workflows;
using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Hosting;
using Ananke.Federation.Monitoring;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class FederatedComplexityMonitorTests
{
    private InMemoryDeploymentRegistry _registry = null!;
    private StubLocalMonitor _localMonitor = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new InMemoryDeploymentRegistry();
        _localMonitor = new StubLocalMonitor();
    }

    [Test]
    public async Task Local_cell_delegates_to_local_monitor()
    {
        var monitor = new FederatedComplexityMonitor(_localMonitor, _registry);
        var snapshot = await monitor.GetSnapshotAsync("local-cell");

        snapshot.WorkflowName.ShouldBe("local-cell");
        snapshot.ToolCount.ShouldBe(5);
        snapshot.RoutingEntropy.ShouldBe(0.8f);
    }

    [Test]
    public async Task Remote_cell_computes_structural_metrics()
    {
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-1",
            WorkflowName = "remote-cell",
            Platform = "azure-ai",
            Version = "1.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var manifests = new Dictionary<string, WorkflowManifest>
        {
            ["remote-cell"] = WorkflowManifest.Parse([
                "name: remote-cell",
                "models:",
                "  default:",
                "    provider: openai",
                "    model: gpt-4.1",
                "jobs:",
                "  agent1:",
                "    type: agent",
                "    model: default",
                "  agent2:",
                "    type: agent",
                "    model: default",
                "connections:",
                "  - agent1 -> agent2",
            ])
        };

        var monitor = new FederatedComplexityMonitor(_localMonitor, _registry, manifests: manifests);
        var snapshot = await monitor.GetSnapshotAsync("remote-cell");

        snapshot.WorkflowName.ShouldBe("remote-cell");
        snapshot.JobCount.ShouldBe(2);
        snapshot.RoutingEntropy.ShouldBe(0f); // Cannot measure remotely
        snapshot.ContextUtilization.ShouldBe(0f); // Cannot measure remotely
    }

    [Test]
    public async Task Remote_cell_enriched_with_telemetry()
    {
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-1",
            WorkflowName = "monitored-cell",
            Platform = "azure-ai",
            Version = "1.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var remoteMonitor = new StubRemoteCellMonitor("azure-ai",
            health: new RemoteCellHealth
            {
                DeploymentId = "dep-1",
                IsHealthy = true,
                LastHeartbeat = DateTimeOffset.UtcNow,
                ErrorCount = 0,
                LatencyMs = 250.0
            },
            metrics: new RemoteCellMetrics
            {
                DeploymentId = "dep-1",
                ExecutionCount = 100,
                TotalTokens = 50000,
                ToolCallCount = 300,
                ErrorRate = 0.02,
                MeasuredAt = DateTimeOffset.UtcNow
            });

        var monitor = new FederatedComplexityMonitor(
            _localMonitor, _registry, [remoteMonitor]);

        var snapshot = await monitor.GetSnapshotAsync("monitored-cell");

        snapshot.AvgLatencyMs.ShouldBe(250f);
        snapshot.AvgCostPerExecution.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Async_snapshot_works_for_remote_cell()
    {
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-async",
            WorkflowName = "async-cell",
            Platform = "vertex-ai",
            Version = "1.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var monitor = new FederatedComplexityMonitor(_localMonitor, _registry);
        var snapshot = await monitor.GetSnapshotAsync("async-cell");

        snapshot.WorkflowName.ShouldBe("async-cell");
        snapshot.RoutingEntropy.ShouldBe(0f);
    }

    [Test]
    public async Task Stopped_deployment_treated_as_local()
    {
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-stopped",
            WorkflowName = "stopped-cell",
            Platform = "azure-ai",
            Version = "1.0",
            Status = DeploymentStatus.Stopped,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var monitor = new FederatedComplexityMonitor(_localMonitor, _registry);
        var snapshot = await monitor.GetSnapshotAsync("stopped-cell");

        // Should delegate to local monitor (stub returns ToolCount=5)
        snapshot.ToolCount.ShouldBe(5);
        snapshot.RoutingEntropy.ShouldBe(0.8f);
    }

    // ── IRemoteCellSource ──────────────────────────────────────────

    [Test]
    public async Task GetRemoteCellNames_returns_active_deployments()
    {
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-1",
            WorkflowName = "remote-a",
            Platform = "azure-ai",
            Version = "1.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-2",
            WorkflowName = "remote-b",
            Platform = "vertex-ai",
            Version = "1.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-3",
            WorkflowName = "stopped-cell",
            Platform = "claude",
            Version = "1.0",
            Status = DeploymentStatus.Stopped,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var monitor = new FederatedComplexityMonitor(_localMonitor, _registry);
        var names = await monitor.GetRemoteCellNamesAsync();

        names.Count.ShouldBe(2);
        names.ShouldContain("remote-a");
        names.ShouldContain("remote-b");
        names.ShouldNotContain("stopped-cell");
    }

    [Test]
    public async Task GetRemoteCellNames_empty_when_no_deployments()
    {
        var monitor = new FederatedComplexityMonitor(_localMonitor, _registry);
        var names = await monitor.GetRemoteCellNamesAsync();

        names.ShouldBeEmpty();
    }

    // ── Test doubles ─────────────────────────────────────────────────

    private sealed class StubLocalMonitor : IHealthMonitor
    {
        public void Record<TState>(WorkflowExecution<TState> execution) { }

        public Task<ComplexitySnapshot> GetSnapshotAsync(string workflowName, CancellationToken ct = default) =>
            Task.FromResult(new ComplexitySnapshot
            {
                WorkflowName = workflowName,
                ToolCount = 5,
                JobCount = 2,
                TagClusterCount = 2,
                RoutingEntropy = 0.8f,
                ResourceSpan = 3,
                ContextUtilization = 0.4f,
                MeasuredAt = DateTimeOffset.UtcNow
            });

        public Task<HealthSnapshot?> GetHealthSnapshotAsync(string workflowName, CancellationToken ct = default) =>
            Task.FromResult<HealthSnapshot?>(null);
    }

    private sealed class StubRemoteCellMonitor(
        string platform,
        RemoteCellHealth health,
        RemoteCellMetrics metrics) : IRemoteCellMonitor
    {
        public string Platform => platform;

        public Task<RemoteCellHealth> GetHealthAsync(string deploymentId, CancellationToken ct = default)
            => Task.FromResult(health);

        public Task<RemoteCellMetrics> GetMetricsAsync(string deploymentId, CancellationToken ct = default)
            => Task.FromResult(metrics);
    }
}
