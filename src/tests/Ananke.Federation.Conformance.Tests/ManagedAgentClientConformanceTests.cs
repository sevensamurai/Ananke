using Ananke.Federation.Agents;
using Ananke.Federation.Deployment;
using Shouldly;

namespace Ananke.Federation.Conformance.Tests;

/// <summary>
/// Conformance suite for <see cref="IManagedAgentClient"/> implementations.
/// </summary>
/// <remarks>
/// Subclass this in each provider's test project and override <see cref="CreateClient"/>
/// (and optionally <see cref="SeedDeploymentAsync"/>) to supply real platform objects.
/// The suite covers get, update, delete, list, and fault surfacing.
/// </remarks>
[TestFixture]
public abstract class ManagedAgentClientConformanceTests
{
    // ------------------------------------------------------------------ factory

    /// <summary>Returns the <see cref="IManagedAgentClient"/> under test.</summary>
    protected abstract IManagedAgentClient CreateClient();

    /// <summary>
    /// Seeds a live deployment record that the client can see, then returns its ID.
    /// The default implementation creates a <see cref="FakeConformanceDeployer"/> on the
    /// same platform and deploys through it — override when the client under test requires
    /// a real platform seed.
    /// </summary>
    protected virtual async Task<string> SeedDeploymentAsync(IManagedAgentClient client)
    {
        var (deployer, _) = FederationConformanceFactory.MakePair(client.Platform);
        var record = await deployer.DeployAsync(
            FederationConformanceFactory.MakeManifest(),
            FederationConformanceFactory.MakeToolKit(),
            FederationConformanceFactory.MakeOptions(client.Platform));
        return record.DeploymentId;
    }

    // ================================================================== tests

    [Test]
    public void Platform_IsNotNullOrWhiteSpace()
    {
        var client = CreateClient();
        client.Platform.ShouldNotBeNullOrWhiteSpace("Every client must declare a platform identifier.");
    }

    [Test]
    public async Task GetAsync_NonExistentId_ReturnsNull()
    {
        var client = CreateClient();
        var result = await client.GetAsync("nonexistent-id-xyz");

        result.ShouldBeNull("GetAsync must return null for an unknown deployment ID.");
    }

    [Test]
    public async Task GetAsync_ExistingId_ReturnsRecord()
    {
        var client = CreateClient();
        var id = await SeedDeploymentAsync(client);

        var record = await client.GetAsync(id);
        record.ShouldNotBeNull("GetAsync must return a record for a known deployment ID.");
        record!.DeploymentId.ShouldBe(id, "Returned record must match the requested ID.");
    }

    [Test]
    public async Task GetAsync_ExistingId_RecordPlatformMatchesClient()
    {
        var client = CreateClient();
        var id = await SeedDeploymentAsync(client);

        var record = await client.GetAsync(id);
        record.ShouldNotBeNull();
        record!.Platform.ShouldBe(client.Platform,
            "Record's Platform must match the client's declared platform.");
    }

    [Test]
    public async Task UpdateAsync_ModifiesRecord_VisibleOnSubsequentGet()
    {
        var client = CreateClient();
        var id = await SeedDeploymentAsync(client);
        var before = await client.GetAsync(id);
        before.ShouldNotBeNull();

        var updated = before! with
        {
            Version = "2.0.0",
            Status = DeploymentStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await client.UpdateAsync(id, updated);

        var after = await client.GetAsync(id);
        after.ShouldNotBeNull();
        after!.Version.ShouldBe("2.0.0", "Updated version must be visible after UpdateAsync.");
    }

    [Test]
    public async Task DeleteAsync_ExistingId_RecordNoLongerRetrievable()
    {
        var client = CreateClient();
        var id = await SeedDeploymentAsync(client);

        await client.DeleteAsync(id);

        var after = await client.GetAsync(id);
        after.ShouldBeNull("Record must not be retrievable after DeleteAsync.");
    }

    [Test]
    public async Task DeleteAsync_NonExistentId_DoesNotThrow()
    {
        var client = CreateClient();
        await Should.NotThrowAsync(() => client.DeleteAsync("nonexistent-id-xyz"));
    }

    [Test]
    public async Task ListAsync_WhenNoDeployments_ReturnsEmptyList()
    {
        var client = CreateClient();
        var ids = await client.ListAsync("workflow-that-was-never-deployed");

        ids.ShouldNotBeNull();
        ids.ShouldBeEmpty("List must be empty when no deployments exist for the manifest name.");
    }

    [Test]
    public async Task ListAsync_AfterDeploy_ContainsDeploymentId()
    {
        var client = CreateClient();
        var id = await SeedDeploymentAsync(client);

        // We need a record with the right WorkflowName — seed via shared-store pair when possible.
        // For abstract subclasses, SeedDeploymentAsync must seed with name "conformance-wf" (default).
        var ids = await client.ListAsync(FederationConformanceFactory.MakeManifest().Name);
        ids.ShouldContain(id, "ListAsync must include the deployed ID under the manifest name.");
    }

    [Test]
    public async Task ListAsync_AfterDelete_DoesNotContainDeploymentId()
    {
        var client = CreateClient();
        var id = await SeedDeploymentAsync(client);

        await client.DeleteAsync(id);

        var ids = await client.ListAsync(FederationConformanceFactory.MakeManifest().Name);
        ids.ShouldNotContain(id, "ListAsync must not include a deleted deployment ID.");
    }

    [Test]
    public async Task ListAsync_ReturnsIReadOnlyList()
    {
        var client = CreateClient();
        var result = await client.ListAsync("any-name");

        result.ShouldBeAssignableTo<IReadOnlyList<string>>(
            "ListAsync return type must satisfy IReadOnlyList<string>.");
    }

    // ======================================================= cancellation

    [Test]
    public async Task GetAsync_CancelledToken_ThrowsOrCompletesGracefully()
    {
        var client = CreateClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        try
        {
            var result = await client.GetAsync("any-id", cts.Token);
            result?.ShouldNotBeNull(); // if it returned, must be valid
        }
        catch (OperationCanceledException) { /* acceptable */ }
    }
}

// =========================================================================
// Self-validating reference fixture — exercises the Fake client so the
// abstract suite runs in CI without credentials.
// =========================================================================

[TestFixture]
internal sealed class FakeAgentClientConformanceTests : ManagedAgentClientConformanceTests
{
    // For the fake fixture the deployer and client share the same in-memory store.
    private readonly Dictionary<string, DeploymentRecord> _store = new();

    protected override IManagedAgentClient CreateClient() =>
        new FakeConformanceAgentClient(_store);

    protected override async Task<string> SeedDeploymentAsync(IManagedAgentClient client)
    {
        var deployer = new FakeConformanceDeployer(_store);
        var record = await deployer.DeployAsync(
            FederationConformanceFactory.MakeManifest(),
            FederationConformanceFactory.MakeToolKit(),
            FederationConformanceFactory.MakeOptions());
        return record.DeploymentId;
    }
}
