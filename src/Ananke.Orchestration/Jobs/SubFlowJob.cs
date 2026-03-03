using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Execution;

namespace Ananke.Orchestration.Jobs;

/// <summary>
/// Internal interface implemented by <see cref="SubFlowJob{TParent, TChild}"/> so the parent
/// <see cref="Workflow{TState}"/> builder can propagate infrastructure (checkpoint store, tracer)
/// at build time.
/// </summary>
internal interface ISubFlowConfiguration
{
    void ConfigureInfrastructure(ICheckpointStore? checkpointStore, IWorkflowTracer? tracer, bool storeCompletions);
}

/// <summary>
/// A job that executes a nested workflow, mapping state between parent and child types.
/// Created via <see cref="Workflow{TState}.SubFlow{TChild}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The inner workflow gets its own <see cref="WorkflowExecution{TState}"/> and trace scope,
/// but shares the parent's <see cref="ICheckpointStore"/> and <see cref="IWorkflowTracer"/>
/// (configured automatically by the parent builder).
/// </para>
/// <para>
/// If the inner workflow is interrupted, a <see cref="SubFlowInterruptedException"/> is thrown,
/// causing the parent <see cref="WorkflowRunner"/> to checkpoint and return with
/// <see cref="ExecutionStatus.Interrupted"/>. On resume the SubFlow re-runs the inner workflow
/// from the beginning.
/// </para>
/// </remarks>
public sealed class SubFlowJob<TParent, TChild> : IJob<TParent>, ISubFlowConfiguration
{
    private readonly Func<TParent, TChild> _mapIn;
    private readonly Func<TParent, TChild, TParent> _mapOut;
    private readonly int _maxDepth;
    private readonly Lazy<WorkflowDefinition<TChild>> _definition;

    private ICheckpointStore? _checkpointStore;
    private IWorkflowTracer? _tracer;
    private bool _storeCompletions = true;

    public string Name { get; }

    public SubFlowJob(
        string name,
        Workflow<TChild> innerWorkflow,
        Func<TParent, TChild> mapIn,
        Func<TParent, TChild, TParent> mapOut,
        int maxDepth = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(innerWorkflow);
        ArgumentNullException.ThrowIfNull(mapIn);
        ArgumentNullException.ThrowIfNull(mapOut);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDepth, 0);

        Name = name;
        _mapIn = mapIn;
        _mapOut = mapOut;
        _maxDepth = maxDepth;
        _definition = new Lazy<WorkflowDefinition<TChild>>(() => innerWorkflow.Build());
    }

    void ISubFlowConfiguration.ConfigureInfrastructure(
        ICheckpointStore? checkpointStore, IWorkflowTracer? tracer, bool storeCompletions)
    {
        _checkpointStore = checkpointStore;
        _tracer = tracer;
        _storeCompletions = storeCompletions;
    }

    public async Task<TParent> ExecuteAsync(TParent state, CancellationToken ct)
    {
        var depth = SubFlowContext.CurrentDepth;
        if (depth >= _maxDepth)
            throw new InvalidOperationException(
                $"SubFlow depth limit ({_maxDepth}) exceeded at '{Name}'. " +
                "Check for infinite recursion or increase the depth limit.");

        SubFlowContext.CurrentDepth = depth + 1;
        try
        {
            var childState = _mapIn(state);
            var definition = _definition.Value;

            // Propagate infrastructure to nested SubFlowJobs (recursive subflows)
            foreach (var descriptor in definition.Jobs.Values)
            {
                if (descriptor.Job is ISubFlowConfiguration nested)
                    nested.ConfigureInfrastructure(_checkpointStore, _tracer, _storeCompletions);
            }

            var runner = new WorkflowRunner(
                _checkpointStore,
                tracer: _tracer,
                storeCompletions: _storeCompletions);

            var execution = await runner.RunAsync(definition, childState, ct);

            return execution.Status switch
            {
                ExecutionStatus.Completed => _mapOut(state, execution.State),
                ExecutionStatus.Interrupted => throw new SubFlowInterruptedException(Name, execution.Id),
                ExecutionStatus.Faulted => throw execution.Result?.Exception
                    ?? new InvalidOperationException($"SubFlow '{Name}' faulted: {execution.Result?.Error}"),
                ExecutionStatus.Cancelled => throw new OperationCanceledException(
                    $"SubFlow '{Name}' was cancelled."),
                _ => throw new InvalidOperationException(
                    $"SubFlow '{Name}' ended with unexpected status: {execution.Status}")
            };
        }
        finally
        {
            SubFlowContext.CurrentDepth = depth;
        }
    }
}
