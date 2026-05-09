using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Streaming;
using System.Runtime.CompilerServices;

namespace Ananke.Organics.Kernel;

/// <summary>
/// Execution wrapper that observes <see cref="Workflow{TState}"/> runs
/// and feeds results into an <see cref="OrganicHost"/> for complexity
/// monitoring and division signaling. Created via
/// <see cref="OrganicWorkflowExtensions"/>.
/// </summary>
/// <remarks>
/// The inner workflow is never modified — its runner, checkpointing,
/// tracing, and middleware are all preserved. Observation happens at
/// the execution boundary (after <c>RunAsync</c> returns).
/// </remarks>
public sealed class OrganicWorkflow<TState>(
    Workflow<TState> inner,
    OrganicHost host,
    string workflowName)
{
    /// <summary>The underlying workflow.</summary>
    public Workflow<TState> Inner => inner;

    /// <summary>Executes the workflow and records the result in the host.</summary>
    public async Task<WorkflowExecution<TState>> RunAsync(
        TState initialState, CancellationToken ct = default)
    {
        var execution = await inner.RunAsync(initialState, ct);
        host.ObserveExecution(workflowName, execution);
        return execution;
    }

    /// <summary>Streams events and records on completion.</summary>
    public async IAsyncEnumerable<WorkflowEvent<TState>> StreamAsync(
        TState initialState,
        WorkflowStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in inner.StreamAsync(initialState, options, ct))
        {
            yield return evt;
            if (evt is WorkflowCompleted<TState> completed)
                host.ObserveCompleted(workflowName, completed);
        }
    }

    /// <summary>Resumes from a checkpoint and records the result.</summary>
    public async Task<WorkflowExecution<TState>> ResumeAsync(
        string executionId, CancellationToken ct = default)
    {
        var execution = await inner.ResumeAsync(executionId, ct);
        host.ObserveExecution(workflowName, execution);
        return execution;
    }
}
