using Ananke.Federation.Deployment;
using Shouldly;

namespace Ananke.Tool.Platform.Adapter.Tests;

/// <summary>
/// Smoke tests that verify each companion adapter module initializer runs exactly once
/// and registers exactly the expected platform string into <see cref="FederationDeployerRegistry"/>.
/// </summary>
[TestFixture]
public sealed class ModuleInitTests
{
    [SetUp]
    public void SetUp()
    {
        FederationDeployerRegistry.Reset();
        Azure.ModuleInit.Initialize();
        Google.ModuleInit.Initialize();
        Anthropic.ModuleInit.Initialize();

        // Default env vars required by the Azure and Google factories.
        // Individual tests override or clear these as needed.
        Environment.SetEnvironmentVariable("AZURE_AI_ENDPOINT",
            "https://example.services.ai.azure.com/api/projects/proj");
        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "my-project");
    }

    [TearDown]
    public void TearDown()
    {
        FederationDeployerRegistry.Reset();
        // Restore env vars that individual tests may have modified
        Environment.SetEnvironmentVariable("AZURE_AI_ENDPOINT", null);
        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", null);
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
    }

    // ── Factory registration ──────────────────────────────────────────────────

    [Test]
    public void Azure_module_init_registers_azure_ai_factory()
    {
        FederationDeployerRegistry.RegisteredFactoryPlatforms
            .ShouldContain("azure-ai", StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void Google_module_init_registers_vertex_ai_factory()
    {
        FederationDeployerRegistry.RegisteredFactoryPlatforms
            .ShouldContain("vertex-ai", StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void Anthropic_module_init_registers_claude_factory()
    {
        FederationDeployerRegistry.RegisteredFactoryPlatforms
            .ShouldContain("claude", StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void Exactly_three_factories_registered()
    {
        FederationDeployerRegistry.RegisteredFactoryPlatforms.Count.ShouldBe(3);
    }

    [Test]
    public void Each_platform_registered_exactly_once()
    {
        var platforms = FederationDeployerRegistry.RegisteredFactoryPlatforms;
        var distinct = platforms.Select(p => p.ToLowerInvariant()).Distinct().ToList();
        distinct.Count.ShouldBe(platforms.Count);
    }

    // ── Materialization ───────────────────────────────────────────────────────

    [Test]
    public void MaterializeFactories_produces_deployer_with_correct_platform_for_azure()
    {
        Environment.SetEnvironmentVariable("AZURE_AI_ENDPOINT",
            "https://example.services.ai.azure.com/api/projects/proj");
        var registry = new InMemoryDeploymentRegistry();

        FederationDeployerRegistry.MaterializeFactories(registry);

        FederationDeployerRegistry.TryResolve("azure-ai", out var deployer).ShouldBeTrue();
        deployer!.Platform.ShouldBe("azure-ai");
    }

    [Test]
    public void MaterializeFactories_produces_deployer_with_correct_platform_for_google()
    {
        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "my-project");
        var registry = new InMemoryDeploymentRegistry();

        FederationDeployerRegistry.MaterializeFactories(registry);

        FederationDeployerRegistry.TryResolve("vertex-ai", out var deployer).ShouldBeTrue();
        deployer!.Platform.ShouldBe("vertex-ai");
    }

    [Test]
    public void MaterializeFactories_produces_deployer_with_correct_platform_for_anthropic()
    {
        var registry = new InMemoryDeploymentRegistry();

        FederationDeployerRegistry.MaterializeFactories(registry);

        FederationDeployerRegistry.TryResolve("claude", out var deployer).ShouldBeTrue();
        deployer!.Platform.ShouldBe("claude");
    }

    [Test]
    public void MaterializeFactories_skips_already_registered_deployers()
    {
        // Seed all required env vars so all three factories can materialize
        Environment.SetEnvironmentVariable("AZURE_AI_ENDPOINT",
            "https://example.services.ai.azure.com/api/projects/proj");
        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "my-project");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-test");

        var registry = new InMemoryDeploymentRegistry();

        FederationDeployerRegistry.MaterializeFactories(registry);
        FederationDeployerRegistry.TryResolve("claude", out var first).ShouldBeTrue();

        FederationDeployerRegistry.MaterializeFactories(registry);
        FederationDeployerRegistry.TryResolve("claude", out var second).ShouldBeTrue();

        second.ShouldBeSameAs(first);
    }

    // ── Missing env vars ──────────────────────────────────────────────────────

    [Test]
    public void Azure_factory_throws_when_endpoint_env_var_missing()
    {
        FederationDeployerRegistry.Reset();
        Azure.ModuleInit.Initialize();
        Environment.SetEnvironmentVariable("AZURE_AI_ENDPOINT", null);

        var registry = new InMemoryDeploymentRegistry();
        Should.Throw<InvalidOperationException>(() =>
            FederationDeployerRegistry.MaterializeFactories(registry));
    }

    [Test]
    public void Google_factory_throws_when_project_env_var_missing()
    {
        FederationDeployerRegistry.Reset();
        Google.ModuleInit.Initialize();
        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", null);

        var registry = new InMemoryDeploymentRegistry();
        Should.Throw<InvalidOperationException>(() =>
            FederationDeployerRegistry.MaterializeFactories(registry));
    }
}
