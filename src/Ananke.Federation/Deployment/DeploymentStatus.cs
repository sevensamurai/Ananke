namespace Ananke.Federation.Deployment;

/// <summary>
/// Lifecycle status of a federation deployment.
/// </summary>
public enum DeploymentStatus
{
    /// <summary>Deployment requested but not yet started.</summary>
    Pending,

    /// <summary>Deployment is in progress (provisioning platform resources).</summary>
    Deploying,

    /// <summary>Deployment is live and serving requests.</summary>
    Active,

    /// <summary>Deployment failed during provisioning or at runtime.</summary>
    Failed,

    /// <summary>Deployment was intentionally stopped or torn down.</summary>
    Stopped
}
