using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Tools;

namespace Ananke.Organics.Kernel;

/// <summary>
/// Extension methods for connecting a <see cref="Workflow{TState}"/>
/// to an <see cref="OrganicHost"/> for automatic complexity monitoring.
/// </summary>
public static class OrganicWorkflowExtensions
{
    /// <summary>
    /// Joins this workflow to an organic host. Returns an
    /// <see cref="OrganicWorkflow{TState}"/> whose <c>RunAsync</c>
    /// automatically feeds execution results into the host's
    /// complexity monitor.
    /// </summary>
    /// <param name="workflow">The workflow to monitor.</param>
    /// <param name="host">The organic host to join.</param>
    /// <param name="toolKit">
    /// Tool kit used to derive a structural profile.
    /// When <see langword="null"/>, a minimal profile is registered (tool count = 0).
    /// </param>
    /// <returns>An observed wrapper — call <c>RunAsync</c> on this.</returns>
    public static OrganicWorkflow<TState> JoinHost<TState>(
        this Workflow<TState> workflow,
        OrganicHost host,
        ToolKit? toolKit = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(host);

        var name = workflow.Name;
        host.Register(name, toolKit);
        return new OrganicWorkflow<TState>(workflow, host, name);
    }
}
