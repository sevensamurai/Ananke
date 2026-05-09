namespace Ananke.Federation.Deployment;

/// <summary>
/// Tracks a single deployment of a workflow manifest to a remote platform.
/// </summary>
public sealed record DeploymentRecord
{
    /// <summary>Unique identifier for this deployment.</summary>
    public required string DeploymentId { get; init; }

    /// <summary>Name of the deployed workflow (from the manifest).</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Target platform identifier (e.g. <c>"vertex-ai"</c>, <c>"claude"</c>).</summary>
    public required string Platform { get; init; }

    /// <summary>Platform-specific resource identifier (e.g. Vertex AI agent resource name).</summary>
    public string? PlatformResourceId { get; init; }

    /// <summary>Deployment version string for tracking updates.</summary>
    public required string Version { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public required DeploymentStatus Status { get; init; }

    /// <summary>When the deployment was first requested.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the status was last updated.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Optional environment tags (e.g. <c>"staging"</c>, <c>"prod"</c>).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
