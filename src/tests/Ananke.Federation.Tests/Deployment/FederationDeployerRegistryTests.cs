using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class FederationDeployerRegistryTests
{
    [SetUp]
    public void SetUp() => FederationDeployerRegistry.Reset();

    [TearDown]
    public void TearDown() => FederationDeployerRegistry.Reset();

    private static IFederationDeployer MakeDeployer(string platform) =>
        new StubDeployer(platform);

    private sealed class StubDeployer(string platform) : IFederationDeployer
    {
        public string Platform => platform;
        public Task<DeployabilityReport> ValidateAsync(WorkflowManifest manifest, ToolKit toolKit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DeploymentRecord> DeployAsync(WorkflowManifest manifest, ToolKit toolKit, DeployOptions options, CancellationToken ct = default) => throw new NotImplementedException();
        public Task TeardownAsync(string deploymentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task MarkFailedAsync(string deploymentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Test]
    public void Register_AddsDeployer_RegisteredPlatformsContainsPlatform()
    {
        var deployer = MakeDeployer("test-platform");

        FederationDeployerRegistry.Register(deployer);

        FederationDeployerRegistry.RegisteredPlatforms.ShouldContain("test-platform");
    }

    [Test]
    public void TryResolve_AfterRegister_ReturnsTrueAndDeployer()
    {
        var deployer = MakeDeployer("azure-ai");
        FederationDeployerRegistry.Register(deployer);

        var found = FederationDeployerRegistry.TryResolve("azure-ai", out var resolved);

        found.ShouldBeTrue();
        resolved.ShouldBeSameAs(deployer);
    }

    [Test]
    public void TryResolve_UnknownPlatform_ReturnsFalse()
    {
        var found = FederationDeployerRegistry.TryResolve("unknown", out var resolved);

        found.ShouldBeFalse();
        resolved.ShouldBeNull();
    }

    [Test]
    public void TryResolve_IsCaseInsensitive()
    {
        var deployer = MakeDeployer("Vertex-AI");
        FederationDeployerRegistry.Register(deployer);

        FederationDeployerRegistry.TryResolve("vertex-ai", out var resolved).ShouldBeTrue();
        resolved.ShouldBeSameAs(deployer);
    }

    [Test]
    public void Register_DuplicatePlatform_ThrowsInvalidOperationException()
    {
        FederationDeployerRegistry.Register(MakeDeployer("claude"));

        Should.Throw<InvalidOperationException>(() =>
            FederationDeployerRegistry.Register(MakeDeployer("claude")));
    }

    [Test]
    public void Register_NullDeployer_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            FederationDeployerRegistry.Register(null!));
    }

    [Test]
    public void RegisteredPlatforms_ReturnsSnapshotOfAllRegistered()
    {
        FederationDeployerRegistry.Register(MakeDeployer("platform-a"));
        FederationDeployerRegistry.Register(MakeDeployer("platform-b"));

        var platforms = FederationDeployerRegistry.RegisteredPlatforms;

        platforms.Count.ShouldBe(2);
        platforms.ShouldContain("platform-a");
        platforms.ShouldContain("platform-b");
    }
}
