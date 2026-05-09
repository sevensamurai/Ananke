using Ananke.Design;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Deployment;

/// <summary>
/// An <see cref="IFederationDeployer"/> that simulates deployment entirely in-process.
/// Produces <see cref="DeploymentRecord"/>s with <c>Platform = "local"</c> and stores
/// them in the supplied <see cref="IDeploymentRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use this deployer for CI pipelines that need to exercise the full deploy/teardown
/// flow without network credentials, and for <c>nnke deploy --target local</c>.
/// </para>
/// <para>
/// No remote resources are created. <see cref="TeardownAsync"/> simply marks the
/// record as <see cref="DeploymentStatus.Stopped"/>.
/// </para>
/// </remarks>
public sealed class LocalFederationDeployer(
    IDeploymentRegistry registry) : IFederationDeployer
{

    /// <inheritdoc />
    public string Platform => "local";

    /// <inheritdoc />
    public Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        CancellationToken ct = default)
    {
        // The local deployer has no remote platform constraints — structural
        // validation is not applicable here. Callers that want platform-aware
        // structural checks should use LocalPlatformValidator with an emulated
        // platform set.
        return Task.FromResult(DeployabilityReport.Ok());
    }

    /// <inheritdoc />
    public async Task<DeploymentRecord> DeployAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        DeployOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);
        ArgumentNullException.ThrowIfNull(options);

        var record = new DeploymentRecord
        {
            DeploymentId = Guid.NewGuid().ToString("N"),
            WorkflowName = manifest.Name,
            Platform = "local",
            Version = "local",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Tags = options.Tags
        };

        await registry.RegisterAsync(record, ct).ConfigureAwait(false);
        return record;
    }

    /// <inheritdoc />
    public async Task TeardownAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        await registry.UpdateStatusAsync(deploymentId, DeploymentStatus.Stopped, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        await registry.UpdateStatusAsync(deploymentId, DeploymentStatus.Failed, ct).ConfigureAwait(false);
    }
}
