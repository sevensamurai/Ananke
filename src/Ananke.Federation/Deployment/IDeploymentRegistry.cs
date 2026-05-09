namespace Ananke.Federation.Deployment;

/// <summary>
/// Stores and queries federation deployment records. Tracks what is deployed where.
/// </summary>
public interface IDeploymentRegistry
{
    /// <summary>Registers a new deployment record.</summary>
    Task RegisterAsync(DeploymentRecord record, CancellationToken ct = default);

    /// <summary>Retrieves a deployment by its unique identifier.</summary>
    Task<DeploymentRecord?> GetAsync(string deploymentId, CancellationToken ct = default);

    /// <summary>Lists all deployment records, optionally filtered by workflow name.</summary>
    Task<IReadOnlyList<DeploymentRecord>> ListAsync(string? workflowName = null, CancellationToken ct = default);

    /// <summary>Updates the status (and <c>UpdatedAt</c> timestamp) of an existing deployment.</summary>
    Task UpdateStatusAsync(string deploymentId, DeploymentStatus status, CancellationToken ct = default);

    /// <summary>
    /// Replaces the stored record for <paramref name="record"/>.<see cref="DeploymentRecord.DeploymentId"/>
    /// in full. Use this when platform resource IDs or other fields change after provisioning.
    /// </summary>
    Task UpdateAsync(DeploymentRecord record, CancellationToken ct = default);
}
