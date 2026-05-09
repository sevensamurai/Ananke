using Ananke.Design;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Deployment;

/// <summary>
/// Deploys a workflow manifest and toolkit to a remote platform.
/// Each platform adapter (Vertex AI, Claude, etc.) provides its own implementation.
/// </summary>
public interface IFederationDeployer
{
    /// <summary>Platform identifier this deployer targets (e.g. <c>"vertex-ai"</c>).</summary>
    string Platform { get; }

    /// <summary>
    /// Validates that the manifest and toolkit can be deployed to this platform.
    /// Performs live checks (credentials, model availability, quotas).
    /// </summary>
    Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        CancellationToken ct = default);

    /// <summary>
    /// Deploys the manifest and toolkit to the platform, returning the deployment record.
    /// </summary>
    Task<DeploymentRecord> DeployAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        DeployOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Tears down a previously deployed workflow, releasing platform resources.
    /// </summary>
    Task TeardownAsync(string deploymentId, CancellationToken ct = default);

    /// <summary>
    /// Records a deployment as <see cref="DeploymentStatus.Failed"/> in the registry.
    /// Called by workflow hosts when an unhandled exception escapes
    /// <see cref="DeployAsync"/>. Default implementation delegates to
    /// <see cref="IDeploymentRegistry.UpdateStatusAsync"/> via the deployer's registry.
    /// </summary>
    /// <remarks>
    /// Override when the concrete deployer needs additional platform-side cleanup on failure.
    /// </remarks>
    Task MarkFailedAsync(string deploymentId, CancellationToken ct = default);
}
