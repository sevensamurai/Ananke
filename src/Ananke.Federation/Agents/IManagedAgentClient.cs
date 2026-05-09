using Ananke.Federation.Deployment;

namespace Ananke.Federation.Agents;

/// <summary>
/// CRUD abstraction over a platform's managed agent resources.
/// Each platform adapter provides a concrete implementation consumed by
/// <see cref="Hosting.PlatformWorkflowHostBase"/> and by conformance tests.
/// </summary>
public interface IManagedAgentClient
{
    /// <summary>Platform identifier this client targets (e.g. <c>"azure-ai"</c>).</summary>
    string Platform { get; }

    /// <summary>
    /// Retrieves the current deployment record for the given deployment ID,
    /// or <see langword="null"/> if it does not exist on the platform.
    /// </summary>
    Task<DeploymentRecord?> GetAsync(string deploymentId, CancellationToken ct = default);

    /// <summary>
    /// Updates a live deployment in-place (e.g. refreshes the system prompt or tool list
    /// without full teardown and redeploy).
    /// </summary>
    Task UpdateAsync(string deploymentId, DeploymentRecord record, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes a managed agent resource from the platform.
    /// No-op if the deployment does not exist.
    /// </summary>
    Task DeleteAsync(string deploymentId, CancellationToken ct = default);

    /// <summary>
    /// Lists all deployment IDs that belong to the given workflow manifest name
    /// on this platform.
    /// </summary>
    Task<IReadOnlyList<string>> ListAsync(string manifestName, CancellationToken ct = default);
}
