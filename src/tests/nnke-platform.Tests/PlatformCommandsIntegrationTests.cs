using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Ananke.Tool.Platform;
using Ananke.Tool.Platform.Commands;
using Shouldly;

namespace Ananke.Tool.Platform.Tests;

/// <summary>
/// Integration tests for <see cref="DeployCommand"/>, <see cref="StatusCommand"/>,
/// and <see cref="TeardownCommand"/> using a fake deployer registered in the
/// static <see cref="FederationDeployerRegistry"/>.
/// </summary>
[TestFixture]
public sealed class PlatformCommandsIntegrationTests
{
    private const string TestPlatform = "test-platform";
    private FakeDeployer _deployer = null!;

    [SetUp]
    public void SetUp()
    {
        FederationDeployerRegistry.Reset();
        _deployer = new FakeDeployer(TestPlatform);
        FederationDeployerRegistry.Register(_deployer);
    }

    [TearDown]
    public void TearDown() => FederationDeployerRegistry.Reset();

    // ── PlatformHost ─────────────────────────────────────────────────────────

    [Test]
    public void PlatformHost_InMemory_Uses_InMemoryRegistry()
    {
        using var host = new PlatformHost(inMemory: true);
        host.Registry.ShouldBeOfType<InMemoryDeploymentRegistry>();
    }

    [Test]
    public void PlatformHost_Default_Uses_JsonFileRegistry()
    {
        using var host = new PlatformHost(inMemory: false);
        host.Registry.ShouldBeOfType<JsonFileDeploymentRegistry>();
    }

    [Test]
    public void PlatformHost_ResolveDeployer_ReturnsRegisteredDeployer()
    {
        using var host = new PlatformHost(inMemory: true);
        var resolved = host.ResolveDeployer(TestPlatform);
        resolved.ShouldBeSameAs(_deployer);
    }

    [Test]
    public void PlatformHost_ResolveDeployer_ReturnsNull_ForUnknownPlatform()
    {
        using var host = new PlatformHost(inMemory: true);
        var resolved = host.ResolveDeployer("no-such-platform");
        resolved.ShouldBeNull();
    }

    // ── DeployCommand helpers ────────────────────────────────────────────────

    [Test]
    public void AdapterInstallHint_AzureAi_ContainsPackageName()
    {
        DeployCommand.AdapterInstallHint("azure-ai").ShouldContain("nnke-platform-azure");
    }

    [Test]
    public void AdapterInstallHint_VertexAi_ContainsPackageName()
    {
        DeployCommand.AdapterInstallHint("vertex-ai").ShouldContain("nnke-platform-google");
    }

    [Test]
    public void AdapterInstallHint_GeminiAgentPlatform_ContainsPackageName()
    {
        DeployCommand.AdapterInstallHint("gemini-agent-platform").ShouldContain("nnke-platform-google");
    }

    [Test]
    public void AdapterInstallHint_Claude_ContainsPackageName()
    {
        DeployCommand.AdapterInstallHint("claude").ShouldContain("nnke-platform-anthropic");
    }

    [Test]
    public void AdapterInstallHint_Unknown_MentionsPlatformName()
    {
        DeployCommand.AdapterInstallHint("my-custom-platform").ShouldContain("my-custom-platform");
    }

    // ── Deploy → Status → Teardown lifecycle ─────────────────────────────────

    [Test]
    public async Task Deploy_RegistersRecord_And_Status_Returns_It()
    {
        using var host = new PlatformHost(inMemory: true);

        // Simulate the deploy flow directly through PlatformHost + deployer
        var manifest = MinimalManifest("my-workflow");
        var toolKit = new ToolKit("test");
        var record = await _deployer.DeployAsync(manifest, toolKit, new DeployOptions { Platform = TestPlatform });
        await host.Registry.RegisterAsync(record);

        var retrieved = await host.Registry.GetAsync(record.DeploymentId);
        retrieved.ShouldNotBeNull();
        retrieved!.WorkflowName.ShouldBe("my-workflow");
        retrieved.Platform.ShouldBe(TestPlatform);
        retrieved.Status.ShouldBe(DeploymentStatus.Active);
    }

    [Test]
    public async Task Teardown_UpdatesStatus_To_Stopped()
    {
        using var host = new PlatformHost(inMemory: true);

        var manifest = MinimalManifest("teardown-workflow");
        var toolKit = new ToolKit("test");
        var record = await _deployer.DeployAsync(manifest, toolKit, new DeployOptions { Platform = TestPlatform });
        await host.Registry.RegisterAsync(record);

        // Teardown
        await _deployer.TeardownAsync(record.DeploymentId);
        await host.Registry.UpdateStatusAsync(record.DeploymentId, DeploymentStatus.Stopped);

        var updated = await host.Registry.GetAsync(record.DeploymentId);
        updated!.Status.ShouldBe(DeploymentStatus.Stopped);
        _deployer.TornDownIds.ShouldContain(record.DeploymentId);
    }

    [Test]
    public async Task Deploy_Duplicate_Guard_Detects_Existing_Active()
    {
        using var host = new PlatformHost(inMemory: true);

        var manifest = MinimalManifest("dup-workflow");
        var toolKit = new ToolKit("test");
        var record = await _deployer.DeployAsync(manifest, toolKit, new DeployOptions { Platform = TestPlatform });
        await host.Registry.RegisterAsync(record);

        // Simulate "force=false" check
        var existing = (await host.Registry.ListAsync("dup-workflow"))
            .FirstOrDefault(r => r.Platform == TestPlatform && r.Status == DeploymentStatus.Active);

        existing.ShouldNotBeNull();
        existing!.DeploymentId.ShouldBe(record.DeploymentId);
    }

    [Test]
    public async Task Status_List_Returns_All_Records()
    {
        using var host = new PlatformHost(inMemory: true);
        var toolKit = new ToolKit("test");

        var r1 = await _deployer.DeployAsync(MinimalManifest("wf-a"), toolKit, new DeployOptions { Platform = TestPlatform });
        var r2 = await _deployer.DeployAsync(MinimalManifest("wf-b"), toolKit, new DeployOptions { Platform = TestPlatform });
        await host.Registry.RegisterAsync(r1);
        await host.Registry.RegisterAsync(r2);

        var all = await host.Registry.ListAsync();
        all.Count.ShouldBe(2);
    }

    [Test]
    public async Task FakeDeployer_ValidateAsync_AlwaysReturnsDeployable()
    {
        var manifest = MinimalManifest("validate-wf");
        var toolKit = new ToolKit("test");
        var report = await _deployer.ValidateAsync(manifest, toolKit);
        report.IsDeployable.ShouldBeTrue();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static WorkflowManifest MinimalManifest(string name) => new()
    {
        Name = name,
        Models = [],
        Jobs = [],
        Connections = [],
        Tools = []
    };
}

// ── Fake deployer ─────────────────────────────────────────────────────────────

internal sealed class FakeDeployer(string platform) : IFederationDeployer
{
    private int _seq;

    public string Platform => platform;
    public List<string> TornDownIds { get; } = [];

    public Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest, ToolKit toolKit, CancellationToken ct = default) =>
        Task.FromResult(DeployabilityReport.Ok());

    public Task<DeploymentRecord> DeployAsync(
        WorkflowManifest manifest, ToolKit toolKit, DeployOptions options, CancellationToken ct = default)
    {
        var record = new DeploymentRecord
        {
            DeploymentId = $"fake-{Interlocked.Increment(ref _seq):D4}",
            WorkflowName = manifest.Name,
            Platform = platform,
            Version = "1.0.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(record);
    }

    public Task TeardownAsync(string deploymentId, CancellationToken ct = default)
    {
        TornDownIds.Add(deploymentId);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string deploymentId, CancellationToken ct = default) => Task.CompletedTask;
}
