using Ananke.Federation.Deployment;
using Ananke.Federation.Hosting;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class HybridRouterTests
{
    private InMemoryDeploymentRegistry _registry = null!;

    [SetUp]
    public void SetUp() => _registry = new InMemoryDeploymentRegistry();

    [Test]
    public async Task No_rules_returns_null_for_local()
    {
        var router = new HybridRouter(_registry);
        var result = await router.ResolveAsync("any-cell");
        result.ShouldBeNull();
    }

    [Test]
    public async Task Prefix_rule_matches()
    {
        var router = new HybridRouter(_registry, [
            new RoutingRule { Prefix = "search-", TargetPlatform = "azure-ai" }
        ]);

        (await router.ResolveAsync("search-web")).ShouldBe("azure-ai");
        (await router.ResolveAsync("other")).ShouldBeNull();
    }

    [Test]
    public async Task Suffix_rule_matches()
    {
        var router = new HybridRouter(_registry, [
            new RoutingRule { Suffix = "-gpu", TargetPlatform = "vertex-ai" }
        ]);

        (await router.ResolveAsync("compute-gpu")).ShouldBe("vertex-ai");
        (await router.ResolveAsync("compute-cpu")).ShouldBeNull();
    }

    [Test]
    public async Task Exact_name_takes_priority()
    {
        var router = new HybridRouter(_registry, [
            new RoutingRule { Prefix = "search-", TargetPlatform = "azure-ai" },
            new RoutingRule { ExactName = "search-special", TargetPlatform = "vertex-ai" }
        ]);

        // First rule wins (prefix match comes before exact in list order)
        (await router.ResolveAsync("search-special")).ShouldBe("azure-ai");
    }

    [Test]
    public async Task First_matching_rule_wins()
    {
        var router = new HybridRouter(_registry, [
            new RoutingRule { Prefix = "a-", TargetPlatform = "azure-ai" },
            new RoutingRule { Prefix = "a-", TargetPlatform = "vertex-ai" }
        ]);

        (await router.ResolveAsync("a-cell")).ShouldBe("azure-ai");
    }

    [Test]
    public async Task Active_deployment_overrides_rules()
    {
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-1",
            WorkflowName = "my-cell",
            Platform = "claude",
            Version = "1.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var router = new HybridRouter(_registry, [
            new RoutingRule { ExactName = "my-cell", TargetPlatform = "azure-ai" }
        ]);

        // Active deployment wins over rules
        (await router.ResolveAsync("my-cell")).ShouldBe("claude");
    }

    [Test]
    public async Task Stopped_deployment_does_not_override()
    {
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-1",
            WorkflowName = "my-cell",
            Platform = "claude",
            Version = "1.0",
            Status = DeploymentStatus.Stopped,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var router = new HybridRouter(_registry, [
            new RoutingRule { ExactName = "my-cell", TargetPlatform = "azure-ai" }
        ]);

        (await router.ResolveAsync("my-cell")).ShouldBe("azure-ai");
    }

    [Test]
    public async Task GetActiveDeployments_returns_only_active()
    {
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-1",
            WorkflowName = "cell-a",
            Platform = "azure-ai",
            Version = "1.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _registry.RegisterAsync(new DeploymentRecord
        {
            DeploymentId = "dep-2",
            WorkflowName = "cell-b",
            Platform = "vertex-ai",
            Version = "1.0",
            Status = DeploymentStatus.Stopped,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var router = new HybridRouter(_registry);
        var active = await router.GetActiveDeploymentsAsync();

        active.Count.ShouldBe(1);
        active.ShouldContainKey("cell-a");
    }

    [Test]
    public async Task Catch_all_rule_matches_everything()
    {
        var router = new HybridRouter(_registry, [
            new RoutingRule { Prefix = "special-", TargetPlatform = "azure-ai" },
            new RoutingRule { TargetPlatform = "vertex-ai" } // catch-all: no prefix/suffix/exact
        ]);

        (await router.ResolveAsync("special-cell")).ShouldBe("azure-ai");
        (await router.ResolveAsync("anything-else")).ShouldBe("vertex-ai");
    }
}
