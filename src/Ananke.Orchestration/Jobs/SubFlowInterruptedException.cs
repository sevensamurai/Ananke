namespace Ananke.Orchestration.Jobs;

/// <summary>
/// Thrown by <see cref="SubFlowJob{TParent, TChild}"/> when the inner workflow
/// is interrupted. Caught by <see cref="Execution.WorkflowRunner"/> to bubble
/// the interrupt up to the parent workflow.
/// </summary>
internal sealed class SubFlowInterruptedException(string subFlowName, string innerExecutionId)
    : Exception($"SubFlow '{subFlowName}' was interrupted (inner execution: {innerExecutionId}).")
{
    public string SubFlowName => subFlowName;
    public string InnerExecutionId => innerExecutionId;
}
