using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Hosting;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Logging;

namespace Ananke.Federation.Azure;

/// <summary>
/// <see cref="Ananke.Organics.Kernel.IWorkflowHost"/> that manages cells as Azure AI agents.
/// <see cref="Ananke.Organics.Kernel.IWorkflowHost.StartAsync"/> deploys the manifest to Azure AI Agent Service;
/// <see cref="Ananke.Organics.Kernel.IWorkflowHost.StopAsync"/> tears down the deployment.
/// </summary>
public sealed class AzureWorkflowHost(
    AzureAgentDeployer deployer,
    WorkflowManifest manifest,
    ToolKit toolKit,
    ILogger<AzureWorkflowHost>? logger = null)
    : PlatformWorkflowHostBase(manifest, toolKit, logger)
{
    private readonly AzureAgentDeployer _deployer = deployer ?? throw new ArgumentNullException(nameof(deployer));

    protected override string Platform => _deployer.Platform;

    protected override Task<DeploymentRecord> DeployCoreAsync(
        WorkflowManifest manifest, ToolKit toolKit, DeployOptions options, CancellationToken ct)
        => _deployer.DeployAsync(manifest, toolKit, options, ct);

    protected override Task TeardownCoreAsync(string deploymentId, CancellationToken ct)
        => _deployer.TeardownAsync(deploymentId, ct);

    protected override Task MarkDeploymentFailedAsync(string deploymentId, CancellationToken ct)
        => _deployer.MarkFailedAsync(deploymentId, ct);
}
