using Ananke.Orchestration.Workflows;

namespace Ananke.Orchestration.Streaming;

/// <summary>
/// Configuration for workflow-level event streaming via
/// <see cref="Execution.IWorkflowRunner.StreamAsync{TState}"/> and
/// <see cref="Workflow{TState}.StreamAsync"/>.
/// </summary>
public sealed class WorkflowStreamOptions
{
    /// <summary>
    /// Maximum number of events buffered in the internal channel before
    /// back-pressure is applied to the runner. Default is 100.
    /// </summary>
    public int Capacity { get; init; } = 100;
}
