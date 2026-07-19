using Ananke.Design;
using Ananke.Federation.Agents;
using Ananke.Federation.Deployment;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Conformance.Tests;

/// <summary>
/// In-process reference <see cref="IFederationDeployer"/> for the conformance suite.
/// Stores deployments in a dictionary; never calls a remote platform.
/// </summary>
internal sealed class FakeConformanceDeployer : IFederationDeployer
{
    private readonly Dictionary<string, DeploymentRecord> _store;
    private readonly string _platform;
    private bool _failNextDeploy;

    /// <summary>Creates a self-contained deployer with its own backing store.</summary>
    internal FakeConformanceDeployer(string platform = "fake")
        : this(new Dictionary<string, DeploymentRecord>(), platform) { }

    /// <summary>Creates a deployer sharing the supplied store (used by the factory).</summary>
    internal FakeConformanceDeployer(
        Dictionary<string, DeploymentRecord> sharedStore,
        string platform = "fake")
    {
        _store = sharedStore;
        _platform = platform;
    }

    public string Platform => _platform;

    /// <summary>Makes the next <see cref="DeployAsync"/> call throw <see cref="InvalidOperationException"/>.</summary>
    internal void BreakNextDeploy() => _failNextDeploy = true;

    public Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        CancellationToken ct = default)
    {
        return Task.FromResult(DeployabilityReport.Ok());
    }

    public Task<DeploymentRecord> DeployAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        DeployOptions options,
        CancellationToken ct = default)
    {
        if (_failNextDeploy)
        {
            _failNextDeploy = false;
            throw new InvalidOperationException("Simulated deploy failure.");
        }

        var record = new DeploymentRecord
        {
            DeploymentId = $"{_platform}::{manifest.Name}::{Guid.NewGuid():N}",
            WorkflowName = manifest.Name,
            Platform = _platform,
            PlatformResourceId = $"fake/agents/{manifest.Name}",
            Version = "1.0.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Tags = options.Tags
        };
        _store[record.DeploymentId] = record;
        return Task.FromResult(record);
    }

    public Task TeardownAsync(string deploymentId, CancellationToken ct = default)
    {
        _store.Remove(deploymentId);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string deploymentId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(deploymentId, out var existing))
            _store[deploymentId] = existing with
            {
                Status = DeploymentStatus.Failed,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        return Task.CompletedTask;
    }

    /// <summary>All currently stored deployment records (test helper).</summary>
    internal IReadOnlyDictionary<string, DeploymentRecord> Deployments => _store;
}

/// <summary>
/// In-process reference <see cref="IManagedAgentClient"/> backed by the same
/// store as <see cref="FakeConformanceDeployer"/> when created via
/// <see cref="FederationConformanceFactory.MakePair"/>.
/// </summary>
internal sealed class FakeConformanceAgentClient : IManagedAgentClient
{
    private readonly Dictionary<string, DeploymentRecord> _store;
    private readonly string _platform;
    private bool _failNextGet;

    internal FakeConformanceAgentClient(
        Dictionary<string, DeploymentRecord> store,
        string platform = "fake")
    {
        _store = store;
        _platform = platform;
    }

    public string Platform => _platform;

    /// <summary>Makes the next <see cref="GetAsync"/> call throw.</summary>
    internal void BreakNextGet() => _failNextGet = true;

    public Task<DeploymentRecord?> GetAsync(string deploymentId, CancellationToken ct = default)
    {
        if (_failNextGet)
        {
            _failNextGet = false;
            throw new InvalidOperationException("Simulated get failure.");
        }
        _store.TryGetValue(deploymentId, out var record);
        return Task.FromResult(record);
    }

    public Task UpdateAsync(string deploymentId, DeploymentRecord record, CancellationToken ct = default)
    {
        _store[deploymentId] = record;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string deploymentId, CancellationToken ct = default)
    {
        _store.Remove(deploymentId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(string manifestName, CancellationToken ct = default)
    {
        IReadOnlyList<string> ids = _store.Values
            .Where(r => r.WorkflowName == manifestName)
            .Select(r => r.DeploymentId)
            .ToList();
        return Task.FromResult(ids);
    }
}
