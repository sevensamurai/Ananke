using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Conformance.Tests;

/// <summary>
/// Conformance suite for <see cref="IFederationDeployer"/> implementations.
/// </summary>
/// <remarks>
/// Subclass this in each provider's test project and override <see cref="CreateDeployer"/>
/// to supply the real adapter. The suite covers the full CRUD lifecycle, retry/fault
/// surfacing, and contract invariants.
/// </remarks>
[TestFixture]
public abstract class FederationDeployerConformanceTests
{
    // ------------------------------------------------------------------ factory

    /// <summary>Returns the <see cref="IFederationDeployer"/> under test.</summary>
    protected abstract IFederationDeployer CreateDeployer();

    // ------------------------------------------------------------------ helpers

    private static WorkflowManifest Manifest(string name = "conformance-wf") =>
        FederationConformanceFactory.MakeManifest(name);

    private static ToolKit Kit() =>
        FederationConformanceFactory.MakeToolKit();

    private DeployOptions Opts() =>
        FederationConformanceFactory.MakeOptions(CreateDeployer().Platform);

    // ================================================================== tests

    [Test]
    public void Platform_IsNotNullOrWhiteSpace()
    {
        var deployer = CreateDeployer();
        deployer.Platform.ShouldNotBeNullOrWhiteSpace("Every deployer must declare a platform identifier.");
    }

    [Test]
    public async Task ValidateAsync_MinimalManifest_ReturnsDeployableReport()
    {
        var deployer = CreateDeployer();
        var report   = await deployer.ValidateAsync(Manifest(), Kit());

        report.ShouldNotBeNull();
        report.IsDeployable.ShouldBeTrue("A valid minimal manifest must be reported as deployable.");
    }

    [Test]
    public async Task ValidateAsync_Report_HasNullOrEmptyErrors_WhenDeployable()
    {
        var deployer = CreateDeployer();
        var report   = await deployer.ValidateAsync(Manifest(), Kit());

        if (report.IsDeployable)
            report.Errors.ShouldBeEmpty("Deployable report must not contain error diagnostics.");
    }

    [Test]
    public async Task DeployAsync_ReturnsRecord_WithNonEmptyDeploymentId()
    {
        var deployer = CreateDeployer();
        var record   = await deployer.DeployAsync(Manifest(), Kit(), Opts());

        record.ShouldNotBeNull();
        record.DeploymentId.ShouldNotBeNullOrWhiteSpace("DeploymentId must be non-empty after deploy.");
    }

    [Test]
    public async Task DeployAsync_ReturnsRecord_WithMatchingWorkflowName()
    {
        const string name = "my-workflow";
        var deployer = CreateDeployer();
        var record   = await deployer.DeployAsync(Manifest(name), Kit(), Opts());

        record.WorkflowName.ShouldBe(name, "WorkflowName in record must match the manifest name.");
    }

    [Test]
    public async Task DeployAsync_ReturnsRecord_WithMatchingPlatform()
    {
        var deployer = CreateDeployer();
        var record   = await deployer.DeployAsync(Manifest(), Kit(), Opts());

        record.Platform.ShouldBe(deployer.Platform,
            "Platform in the record must match the deployer's declared platform.");
    }

    [Test]
    public async Task DeployAsync_ReturnsRecord_WithActiveOrDeployingStatus()
    {
        var deployer = CreateDeployer();
        var record   = await deployer.DeployAsync(Manifest(), Kit(), Opts());

        record.Status.ShouldBeOneOf(
            [DeploymentStatus.Active, DeploymentStatus.Deploying],
            "A successful deploy must return Active or Deploying status.");
    }

    [Test]
    public async Task DeployAsync_ReturnsRecord_WithPositiveTimestamps()
    {
        var before   = DateTimeOffset.UtcNow.AddSeconds(-1);
        var deployer = CreateDeployer();
        var record   = await deployer.DeployAsync(Manifest(), Kit(), Opts());

        record.CreatedAt.ShouldBeGreaterThan(before, "CreatedAt must be a recent UTC timestamp.");
        record.UpdatedAt.ShouldBeGreaterThanOrEqualTo(record.CreatedAt,
            "UpdatedAt must not be earlier than CreatedAt.");
    }

    [Test]
    public async Task TeardownAsync_ExistingDeployment_DoesNotThrow()
    {
        var deployer = CreateDeployer();
        var record   = await deployer.DeployAsync(Manifest(), Kit(), Opts());

        await Should.NotThrowAsync(() => deployer.TeardownAsync(record.DeploymentId));
    }

    [Test]
    public async Task TeardownAsync_NonExistentDeployment_DoesNotThrow()
    {
        var deployer = CreateDeployer();
        // Implementations must be idempotent for missing IDs.
        await Should.NotThrowAsync(() => deployer.TeardownAsync("nonexistent-id-xyz"));
    }

    [Test]
    public async Task MarkFailedAsync_ExistingDeployment_DoesNotThrow()
    {
        var deployer = CreateDeployer();
        var record   = await deployer.DeployAsync(Manifest(), Kit(), Opts());

        await Should.NotThrowAsync(() => deployer.MarkFailedAsync(record.DeploymentId));
    }

    [Test]
    public async Task MarkFailedAsync_NonExistentDeployment_DoesNotThrow()
    {
        var deployer = CreateDeployer();
        // Marking a missing deployment as failed must be a no-op, not throw.
        await Should.NotThrowAsync(() => deployer.MarkFailedAsync("nonexistent-id-xyz"));
    }

    [Test]
    public async Task DeployAsync_WhenForceTrue_DoesNotThrowOnRedeployment()
    {
        var deployer = CreateDeployer();
        var manifest = Manifest();
        var kit      = Kit();
        var opts     = new DeployOptions { Platform = deployer.Platform, Force = true, Tags = ["conformance"] };

        await deployer.DeployAsync(manifest, kit, opts);
        // Forced re-deploy of the same manifest must not throw.
        await Should.NotThrowAsync(() => deployer.DeployAsync(manifest, kit, opts));
    }

    [Test]
    public async Task DeployAsync_Tags_ArePropagatedToRecord()
    {
        var deployer = CreateDeployer();
        var opts     = new DeployOptions
        {
            Platform = deployer.Platform,
            Tags     = ["tag-a", "tag-b"]
        };
        var record = await deployer.DeployAsync(Manifest(), Kit(), opts);

        record.Tags.ShouldContain("tag-a", "Tags supplied in options must appear in the deployment record.");
        record.Tags.ShouldContain("tag-b");
    }

    // ======================================================= cancellation

    [Test]
    public async Task ValidateAsync_CancelledToken_ThrowsOrCompletesGracefully()
    {
        var deployer = CreateDeployer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Either throws OperationCanceledException or completes (fast-path check).
        try
        {
            var report = await deployer.ValidateAsync(Manifest(), Kit(), cts.Token);
            // If it returned, the result must still be non-null.
            report.ShouldNotBeNull();
        }
        catch (OperationCanceledException) { /* acceptable */ }
    }

    [Test]
    public async Task DeployAsync_CancelledToken_ThrowsOrCompletesGracefully()
    {
        var deployer = CreateDeployer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        try
        {
            var record = await deployer.DeployAsync(Manifest(), Kit(), Opts(), cts.Token);
            record.ShouldNotBeNull();
        }
        catch (OperationCanceledException) { /* acceptable */ }
    }
}

// =========================================================================
// Self-validating reference fixture — exercises the Fake deployer so the
// abstract suite runs in CI without credentials.
// =========================================================================

[TestFixture]
internal sealed class FakeDeployerConformanceTests : FederationDeployerConformanceTests
{
    protected override IFederationDeployer CreateDeployer() => new FakeConformanceDeployer();
}
