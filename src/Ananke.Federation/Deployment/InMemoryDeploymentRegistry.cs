using System.Collections.Concurrent;

namespace Ananke.Federation.Deployment;

/// <summary>
/// In-memory implementation of <see cref="IDeploymentRegistry"/> for development and testing.
/// Not suitable for production — state is lost on process exit.
/// </summary>
public sealed class InMemoryDeploymentRegistry : IDeploymentRegistry
{
    private readonly ConcurrentDictionary<string, DeploymentRecord> _records = new();

    /// <inheritdoc />
    public Task RegisterAsync(DeploymentRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!_records.TryAdd(record.DeploymentId, record))
            throw new InvalidOperationException($"Deployment '{record.DeploymentId}' already exists.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DeploymentRecord?> GetAsync(string deploymentId, CancellationToken ct = default)
    {
        _records.TryGetValue(deploymentId, out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeploymentRecord>> ListAsync(string? workflowName = null, CancellationToken ct = default)
    {
        IReadOnlyList<DeploymentRecord> results = workflowName is null
            ? _records.Values.ToList()
            : _records.Values.Where(r => r.WorkflowName == workflowName).ToList();

        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task UpdateStatusAsync(string deploymentId, DeploymentStatus status, CancellationToken ct = default)
    {
        if (!_records.TryGetValue(deploymentId, out var existing))
            throw new KeyNotFoundException($"Deployment '{deploymentId}' not found.");

        var updated = existing with { Status = status, UpdatedAt = DateTimeOffset.UtcNow };
        _records[deploymentId] = updated;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(DeploymentRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!_records.ContainsKey(record.DeploymentId))
            throw new KeyNotFoundException($"Deployment '{record.DeploymentId}' not found.");

        _records[record.DeploymentId] = record;
        return Task.CompletedTask;
    }
}
