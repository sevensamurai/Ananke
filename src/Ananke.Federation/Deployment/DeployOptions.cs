namespace Ananke.Federation.Deployment;

/// <summary>
/// Options controlling a federation deployment operation.
/// </summary>
public sealed record DeployOptions
{
    /// <summary>
    /// When <see langword="true"/>, forces re-deployment even if the platform
    /// already has an active deployment for this workflow.
    /// </summary>
    public bool Force { get; init; }

    /// <summary>Target platform identifier (e.g. <c>"vertex-ai"</c>, <c>"claude"</c>).</summary>
    public required string Platform { get; init; }

    /// <summary>Optional environment tags to attach to the deployment record.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
