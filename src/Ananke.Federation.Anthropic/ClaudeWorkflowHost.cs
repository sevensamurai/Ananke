using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Hosting;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Logging;

namespace Ananke.Federation.Anthropic;

/// <summary>
/// <see cref="Ananke.Organics.Kernel.IWorkflowHost"/> that manages cells as Claude managed agents.
/// <see cref="Ananke.Organics.Kernel.IWorkflowHost.StartAsync"/> deploys the manifest to Anthropic;
/// <see cref="Ananke.Organics.Kernel.IWorkflowHost.StopAsync"/> tears down the deployment.
/// </summary>
public sealed class ClaudeWorkflowHost(
    ClaudeDeployer deployer,
    WorkflowManifest manifest,
    ToolKit toolKit,
    ILogger<ClaudeWorkflowHost>? logger = null)
    : PlatformWorkflowHostBase(manifest, toolKit, logger)
{
    private readonly ClaudeDeployer _deployer = deployer ?? throw new ArgumentNullException(nameof(deployer));

    protected override string Platform => _deployer.Platform;

    protected override Task<DeploymentRecord> DeployCoreAsync(
        WorkflowManifest manifest, ToolKit toolKit, DeployOptions options, CancellationToken ct)
        => _deployer.DeployAsync(manifest, toolKit, options, ct);

    protected override Task TeardownCoreAsync(string deploymentId, CancellationToken ct)
        => _deployer.TeardownAsync(deploymentId, ct);

    protected override Task MarkDeploymentFailedAsync(string deploymentId, CancellationToken ct)
        => _deployer.MarkFailedAsync(deploymentId, ct);
}
