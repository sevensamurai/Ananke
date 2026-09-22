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
    public void Azure_module_init_registers_azure_factory()
    {
        FederationDeployerRegistry.RegisteredFactoryPlatforms
            .ShouldContain("azure", StringComparer.OrdinalIgnoreCase);
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

        // Deliberately resolved by the legacy identifier: it is a permanent alias, and a
        // deployment registered before the rename must still find its deployer.
        FederationDeployerRegistry.TryResolve("azure-ai", out var deployer).ShouldBeTrue();
        deployer!.Platform.ShouldBe("azure");
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
    public void Azure_factory_failure_is_contained_and_reported()
    {
        FederationDeployerRegistry.Reset();
        Azure.ModuleInit.Initialize();
        Environment.SetEnvironmentVariable("AZURE_AI_ENDPOINT", null);

        AssertContained("azure", "AZURE_AI_ENDPOINT");
    }

    [Test]
    public void Google_factory_failure_is_contained_and_reported()
    {
        FederationDeployerRegistry.Reset();
        Google.ModuleInit.Initialize();
        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", null);

        AssertContained("vertex-ai", "GOOGLE_CLOUD_PROJECT");
    }

    /// <summary>
    /// These two used to assert that <c>MaterializeFactories</c> <i>threw</i>. The factory still
    /// does — it genuinely cannot construct a deployer without its configuration, and the message
    /// naming the variable is the useful part. What changed is that materialization no longer lets
    /// that escape.
    /// </summary>
    /// <remarks>
    /// The inversion is deliberate and is not a weakening. While module initializers never fired, no
    /// factory ran and the old contract was untestable in practice; the moment the probe was fixed,
    /// a single unconfigured adapter crashed <b>every</b> command — <c>adapters doctor</c> included,
    /// and commands aimed at other platforms included. Contained, not silenced: the platform does
    /// not resolve, and the factory's own message reaches the caller.
    /// </remarks>
    private static void AssertContained(string platform, string expectedInMessage)
    {
        var registry = new InMemoryDeploymentRegistry();
        var failures = new List<(string Platform, Exception Error)>();

        Should.NotThrow(() => FederationDeployerRegistry.MaterializeFactories(
            registry, (p, e) => failures.Add((p, e))));

        FederationDeployerRegistry.TryResolve(platform, out _).ShouldBeFalse();

        var failure = failures.ShouldHaveSingleItem();
        failure.Platform.ShouldBe(platform);
        failure.Error.Message.ShouldContain(expectedInMessage);
    }
}
