using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tracing;

namespace Ananke.Orchestration;

public static class Workflow
{
    internal const string EndMarker = "__end__";

    public static string End => EndMarker;

    public static IRouter<TState> Decide<TState>(Func<TState, string> route) =>
        new DelegateRouter<TState>(route);

    public static IRouter<TState> DecideAsync<TState>(Func<TState, Task<string>> route) =>
        new AsyncDelegateRouter<TState>(route);

    /// <summary>
    /// Creates a fluent builder for an LLM-driven <see cref="AgentRouter{TState}"/>.
    /// The agent receives a description of the current state and the available route options,
    /// optionally calls tools to gather information, and returns the next job name.
    /// </summary>
    /// <example>
    /// <code>
    /// .Then("analyze", Workflow.DecideWithAgent&lt;MyState&gt;(model)
    ///     .WithPrompt(s =&gt; $"Data quality: {s.Score}")
    ///     .WithOptions("enrich", "validate", Workflow.End)
    ///     .Build())
    /// </code>
    /// </example>
    public static AgentRouter<TState>.Builder DecideWithAgent<TState>(IAgentModel model) =>
        new(model);

    /// <summary>
    /// Creates a fork target for parallel execution. Use with
    /// <see cref="Workflow{TState}.Then(string, ForkTarget)"/> to fan out to multiple jobs.
    /// </summary>
    public static ForkTarget Fork(params string[] targets) => new(targets);

    /// <summary>
    /// Creates a fork target with explicit cancellation mode for parallel execution.
    /// </summary>
    public static ForkTarget Fork(ForkMode mode, params string[] targets) => new(targets, mode);
}

/// <summary>
/// Fluent builder for constructing and running a typed workflow.
/// </summary>
/// <remarks>
/// <b>Thread safety:</b> <c>Workflow&lt;TState&gt;</c> is not thread-safe.
/// Construct and configure your workflow on a single thread before calling
/// <see cref="RunAsync"/> or <see cref="Build"/>.
/// </remarks>
public sealed class Workflow<TState>
{
    private readonly string _name;
    private readonly Dictionary<string, JobDescriptor<TState>> _jobs = [];
    private readonly List<Connection> _connections = [];
    private readonly Dictionary<string, Func<TState, Task>?> _onEnterActions = [];
    private readonly Dictionary<string, Func<TState, Task>?> _onExitActions = [];
    private readonly Dictionary<string, TimeSpan> _timeouts = [];
    private readonly Dictionary<string, InterruptMode> _interrupts = [];
    private readonly List<JoinDescriptor<TState>> _joins = [];
    private string? _entryJob;
    private IWorkflowRunner? _runner;
    private ICheckpointStore? _checkpointStore;
    private IWorkflowTracer? _tracer;
    private bool _storeCompletions = true;
    private Dictionary<string, string>? _metadata;

    public Workflow(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public Workflow<TState> Job(string name, Func<TState, CancellationToken, Task<TState>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_jobs.ContainsKey(name))
            throw new InvalidOperationException($"Job '{name}' is already defined.");

        _entryJob ??= name;

        _jobs[name] = new JobDescriptor<TState>
        {
            Name = name,
            Job = new DelegateJob<TState>(name, execute)
        };

        return this;
    }

    public Workflow<TState> Job(string name, IJob<TState> job)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(job);

        if (_jobs.ContainsKey(name))
            throw new InvalidOperationException($"Job '{name}' is already defined.");

        _entryJob ??= name;

        _jobs[name] = new JobDescriptor<TState>
        {
            Name = name,
            Job = job
        };

        return this;
    }

    public Workflow<TState> Then(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        _connections.Add(new DirectConnection { From = from, To = to });
        return this;
    }

    public Workflow<TState> Then(string from, IRouter<TState> router)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentNullException.ThrowIfNull(router);

        _connections.Add(new RouterConnection<TState> { From = from, Router = router });
        return this;
    }

    /// <summary>
    /// Connects a job to a fork target for parallel execution.
    /// Each target job runs concurrently as an independent branch.
    /// </summary>
    public Workflow<TState> Then(string from, ForkTarget fork)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentNullException.ThrowIfNull(fork);

        _connections.Add(new ForkConnection { From = from, Targets = fork.Targets, Mode = fork.Mode });
        return this;
    }

    /// <summary>
    /// Defines a fan-in point where parallel branches converge. The <paramref name="merge"/>
    /// function reconciles the final state from each branch into a single state.
    /// For correct results, <typeparamref name="TState"/> should be immutable (e.g. a record).
    /// </summary>
    public Workflow<TState> Join(string[] sources, string target, Func<TState[], TState> merge)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(merge);

        if (sources.Length < 2)
            throw new ArgumentException("Join requires at least two sources.", nameof(sources));

        _joins.Add(new JoinDescriptor<TState>(sources, target, merge));
        return this;
    }

    /// <summary>
    /// Registers a nested workflow as a job. The <paramref name="mapIn"/> function
    /// transforms the parent state into the child workflow's state, and <paramref name="mapOut"/>
    /// merges the child result back into the parent state.
    /// </summary>
    /// <remarks>
    /// The inner workflow shares the parent's checkpoint store and tracer (configured automatically).
    /// Recursive subflows are allowed up to <paramref name="maxDepth"/> (default 5).
    /// If the inner workflow is interrupted, the interrupt bubbles up to the parent.
    /// </remarks>
    public Workflow<TState> SubFlow<TChild>(
        string name,
        Workflow<TChild> innerWorkflow,
        Func<TState, TChild> mapIn,
        Func<TState, TChild, TState> mapOut,
        int maxDepth = 5)
    {
        var subFlowJob = new SubFlowJob<TState, TChild>(name, innerWorkflow, mapIn, mapOut, maxDepth);
        return Job(name, subFlowJob);
    }

    /// <summary>
    /// Chains one or more jobs together in a linear sequence.
    /// </summary>
    /// <param name="jobNames"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public Workflow<TState> Chain(params string[] jobNames)
    {
        ArgumentNullException.ThrowIfNull(jobNames);

        if (jobNames.Length < 2)
            throw new ArgumentException("Chain requires at least two job names.", nameof(jobNames));

        for (var i = 0; i < jobNames.Length - 1; i++)
        {
            Then(jobNames[i], jobNames[i + 1]);
        }

        return this;
    }

    public Workflow<TState> OnEnter(string jobName, Func<TState, Task> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        _onEnterActions[jobName] = action;
        return this;
    }

    public Workflow<TState> OnExit(string jobName, Func<TState, Task> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        _onExitActions[jobName] = action;
        return this;
    }

    public Workflow<TState> Timeout(string jobName, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeouts[jobName] = timeout;
        return this;
    }

    /// <summary>
    /// Pauses workflow execution <em>before</em> the specified job runs.
    /// The execution is checkpointed with <see cref="ExecutionStatus.Interrupted"/> status
    /// and can be resumed via <see cref="ResumeAsync(string, CancellationToken)"/>.
    /// Requires <see cref="UseCheckpointing"/> to be configured.
    /// </summary>
    public Workflow<TState> InterruptBefore(string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        _interrupts[jobName] = InterruptMode.Before;
        return this;
    }

    /// <summary>
    /// Pauses workflow execution <em>after</em> the specified job completes.
    /// The execution is checkpointed with <see cref="ExecutionStatus.Interrupted"/> status
    /// and can be resumed via <see cref="ResumeAsync(string, CancellationToken)"/>.
    /// Requires <see cref="UseCheckpointing"/> to be configured.
    /// </summary>
    public Workflow<TState> InterruptAfter(string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        _interrupts[jobName] = InterruptMode.After;
        return this;
    }

    public Workflow<TState> UseRunner(IWorkflowRunner runner)
    {
        _runner = runner;
        return this;
    }

    public Workflow<TState> UseCheckpointing(ICheckpointStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _checkpointStore = store;
        return this;
    }

    public Workflow<TState> UseTracing(IWorkflowTracer tracer)
    {
        ArgumentNullException.ThrowIfNull(tracer);
        _tracer = tracer;
        return this;
    }

    /// <summary>
    /// Controls whether LLM completions are stored in the provider's platform logs
    /// (e.g. <see href="https://platform.openai.com/logs"/>). Default is <c>true</c>.
    /// </summary>
    public Workflow<TState> StoreCompletions(bool enabled)
    {
        _storeCompletions = enabled;
        return this;
    }

    /// <summary>
    /// Attaches workflow-level key/value metadata that flows into
    /// <see cref="WorkflowExecution{TState}.Metadata"/>, checkpoints, and trace attributes.
    /// Useful for correlation IDs, tenant IDs, and user context.
    /// </summary>
    public Workflow<TState> WithMetadata(Dictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
        return this;
    }

    public WorkflowDefinition<TState> Build()
    {
        if (_entryJob is null)
            throw new InvalidOperationException("Workflow must have at least one job.");

        ApplyLifecycleActions();

        return new WorkflowDefinition<TState>(_name, _jobs, _connections, _entryJob, _metadata, _joins);
    }

    public async Task<WorkflowExecution<TState>> RunAsync(
        TState initialState,
        CancellationToken ct = default)
    {
        var definition = Build();
        var runner = _runner ?? new WorkflowRunner(_checkpointStore, tracer: _tracer, storeCompletions: _storeCompletions);
        return await runner.RunAsync(definition, initialState, ct);
    }

    /// <summary>
    /// Builds and executes the workflow, streaming orchestration progress events
    /// as an <see cref="IAsyncEnumerable{T}"/>. Consume via <c>await foreach</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// await foreach (var evt in workflow.StreamAsync(initialState, ct: ct))
    /// {
    ///     switch (evt)
    ///     {
    ///         case JobStarted&lt;MyState&gt; js   =&gt; Console.WriteLine($"▶ {js.JobName}");
    ///         case JobCompleted&lt;MyState&gt; jc =&gt; Console.WriteLine($"✓ {jc.JobName}");
    ///         case WorkflowCompleted&lt;MyState&gt; wc =&gt; Console.WriteLine("Done!");
    ///     }
    /// }
    /// </code>
    /// </example>
    public IAsyncEnumerable<WorkflowEvent<TState>> StreamAsync(
        TState initialState,
        WorkflowStreamOptions? options = null,
        CancellationToken ct = default)
    {
        var definition = Build();
        var runner = _runner ?? new WorkflowRunner(_checkpointStore, tracer: _tracer, storeCompletions: _storeCompletions);
        return runner.StreamAsync(definition, initialState, options, ct);
    }

    public async Task<WorkflowExecution<TState>> ResumeAsync(
        string executionId,
        CancellationToken ct = default)
    {
        if (_checkpointStore is null)
            throw new InvalidOperationException("Checkpointing is not configured. Call UseCheckpointing() first.");

        var checkpoint = await _checkpointStore.LoadAsync<TState>(executionId, ct)
            ?? throw new InvalidOperationException($"No checkpoint found for execution '{executionId}'.");

        var definition = Build();
        var runner = _runner ?? new WorkflowRunner(_checkpointStore, tracer: _tracer, storeCompletions: _storeCompletions);
        return await runner.ResumeAsync(definition, checkpoint, ct);
    }

    /// <summary>
    /// Resumes a previously interrupted execution, applying <paramref name="stateTransform"/>
    /// to the checkpointed state before continuing. Use this to inject human input
    /// (approvals, edits, corrections) into the workflow state.
    /// </summary>
    public async Task<WorkflowExecution<TState>> ResumeAsync(
        string executionId,
        Func<TState, TState> stateTransform,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stateTransform);

        if (_checkpointStore is null)
            throw new InvalidOperationException("Checkpointing is not configured. Call UseCheckpointing() first.");

        var checkpoint = await _checkpointStore.LoadAsync<TState>(executionId, ct)
            ?? throw new InvalidOperationException($"No checkpoint found for execution '{executionId}'.");

        var definition = Build();
        var runner = _runner ?? new WorkflowRunner(_checkpointStore, tracer: _tracer, storeCompletions: _storeCompletions);
        return await runner.ResumeAsync(definition, checkpoint, stateTransform, ct);
    }

    private void ApplyLifecycleActions()
    {
        foreach (var (jobName, action) in _onEnterActions)
        {
            if (_jobs.TryGetValue(jobName, out var descriptor))
                _jobs[jobName] = descriptor with { OnEnter = action };
        }

        foreach (var (jobName, action) in _onExitActions)
        {
            if (_jobs.TryGetValue(jobName, out var descriptor))
                _jobs[jobName] = descriptor with { OnExit = action };
        }

        foreach (var (jobName, timeout) in _timeouts)
        {
            if (_jobs.TryGetValue(jobName, out var descriptor))
                _jobs[jobName] = descriptor with { Timeout = timeout };
        }

        foreach (var (jobName, mode) in _interrupts)
        {
            if (_jobs.TryGetValue(jobName, out var descriptor))
                _jobs[jobName] = descriptor with { Interrupt = mode };
        }

        foreach (var (_, descriptor) in _jobs)
        {
            if (descriptor.Job is ISubFlowConfiguration subflow)
                subflow.ConfigureInfrastructure(_checkpointStore, _tracer, _storeCompletions);
        }
    }
}
