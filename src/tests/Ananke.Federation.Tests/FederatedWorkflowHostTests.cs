using Ananke.Federation.Deployment;
using Ananke.Federation.Hosting;
using Ananke.Organics.Kernel;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class FederatedWorkflowHostTests
{
    private InProcessWorkflowHost _localHost = null!;
    private InProcessWorkflowHost _azureHost = null!;
    private InProcessWorkflowHost _vertexHost = null!;
    private InMemoryDeploymentRegistry _registry = null!;
    private FederatedWorkflowHost _host = null!;

    [SetUp]
    public async Task SetUp()
    {
        _localHost = new InProcessWorkflowHost();
        _azureHost = new InProcessWorkflowHost();
        _vertexHost = new InProcessWorkflowHost();
        _registry = new InMemoryDeploymentRegistry();

        var platformHosts = new Dictionary<string, IWorkflowHost>
        {
            ["azure-ai"] = _azureHost,
            ["vertex-ai"] = _vertexHost
        };

        var rules = new List<RoutingRule>
        {
            new() { Prefix = "search-", TargetPlatform = "azure-ai" },
            new() { Suffix = "-heavy", TargetPlatform = "vertex-ai" },
            new() { ExactName = "code-runner", TargetPlatform = "azure-ai" },
        };

        var router = new HybridRouter(_registry, rules);
        _host = new FederatedWorkflowHost(_localHost, platformHosts, router);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
        await _localHost.DisposeAsync();
        await _azureHost.DisposeAsync();
        await _vertexHost.DisposeAsync();
    }

    [Test]
    public async Task Local_cell_starts_on_local_host()
    {
        await _host.StartAsync("my-cell", WorkflowLoops.Park);

        _localHost.ListActive().ShouldContain("my-cell");
        _azureHost.ListActive().ShouldBeEmpty();
        _host.GetCellPlatform("my-cell").ShouldBeNull();
    }

    [Test]
    public async Task Prefix_rule_routes_to_azure()
    {
        await _host.StartAsync("search-web", WorkflowLoops.Park);

        _azureHost.ListActive().ShouldContain("search-web");
        _localHost.ListActive().ShouldBeEmpty();
        _host.GetCellPlatform("search-web").ShouldBe("azure-ai");
    }

    [Test]
    public async Task Suffix_rule_routes_to_vertex()
    {
        await _host.StartAsync("compute-heavy", WorkflowLoops.Park);

        _vertexHost.ListActive().ShouldContain("compute-heavy");
        _host.GetCellPlatform("compute-heavy").ShouldBe("vertex-ai");
    }

    [Test]
    public async Task Exact_name_rule_routes_correctly()
    {
        await _host.StartAsync("code-runner", WorkflowLoops.Park);

        _azureHost.ListActive().ShouldContain("code-runner");
        _host.GetCellPlatform("code-runner").ShouldBe("azure-ai");
    }

    [Test]
    public async Task Active_deployment_overrides_rules()
    {
        // Register an active deployment for "my-cell" on vertex-ai
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-1",
            WorkflowName = "my-cell",
            Platform = "vertex-ai",
            Version = "1.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await _host.StartAsync("my-cell", WorkflowLoops.Park);

        _vertexHost.ListActive().ShouldContain("my-cell");
        _host.GetCellPlatform("my-cell").ShouldBe("vertex-ai");
    }

    [Test]
    public async Task Stop_removes_from_correct_host()
    {
        await _host.StartAsync("search-api", WorkflowLoops.Park);
        _azureHost.ListActive().ShouldContain("search-api");

        await _host.StopAsync("search-api");

        _azureHost.ListActive().ShouldBeEmpty();
        _host.GetCellPlatform("search-api").ShouldBeNull();
    }

    [Test]
    public async Task ListActive_returns_cells_from_all_hosts()
    {
        await _host.StartAsync("local-cell", WorkflowLoops.Park);
        await _host.StartAsync("search-cell", WorkflowLoops.Park);
        await _host.StartAsync("heavy-cell-heavy", WorkflowLoops.Park);

        var active = _host.ListActive();
        active.Count.ShouldBe(3);
        active.ShouldContain("local-cell");
        active.ShouldContain("search-cell");
        active.ShouldContain("heavy-cell-heavy");
    }

    [Test]
    public async Task StartAsync_routes_correctly()
    {
        await _host.StartAsync("search-async", WorkflowLoops.Park);

        _azureHost.ListActive().ShouldContain("search-async");
        _host.GetCellPlatform("search-async").ShouldBe("azure-ai");
    }

    [Test]
    public async Task Unknown_platform_falls_back_to_local()
    {
        // Create a router with a rule pointing to an unregistered platform
        var rules = new List<RoutingRule>
        {
            new() { Prefix = "x-", TargetPlatform = "unknown-platform" }
        };
        var router = new HybridRouter(_registry, rules);
        // allowFallbackToLocal: true opts into the graceful-degradation path
        // (the default is false so misconfiguration is surfaced immediately — criterion 5).
        var host = new FederatedWorkflowHost(
            _localHost,
            new Dictionary<string, IWorkflowHost> { ["azure-ai"] = _azureHost },
            router,
            allowFallbackToLocal: true);

        await host.StartAsync("x-cell", WorkflowLoops.Park);

        // Falls back to local because "unknown-platform" isn't registered
        _localHost.ListActive().ShouldContain("x-cell");
    }
}
