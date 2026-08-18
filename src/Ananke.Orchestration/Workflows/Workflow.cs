using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tracing;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Streaming;

namespace Ananke.Orchestration.Workflows;

public static class Workflow
{
    internal const string EndMarker = "__end__";

    /// <summary>
    /// Sentinel that explicitly terminates a workflow from inside a router or decision lambda.
    /// A job with no outgoing edge is already implicitly terminal — static edges to
    /// <c>Workflow.End</c> are redundant and can be omitted.
    /// </summary>
    public static string End => EndMarker;

    /// <summary>
    /// Type-safe sentinel for workflow termination. Equivalent to <see cref="End"/>
    /// but returns a <see cref="JobRef"/> for use with router overloads such as
    /// <see cref="Workflow{TState}.Then(JobRef, IRouter{TState})"/>.
    /// A job with no outgoing edge is already implicitly terminal — static edges to
    /// <c>Workflow.EndRef</c> are redundant and can be omitted.
    /// </summary>
    public static JobRef EndRef => new(EndMarker);

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

    /// <summary>
    /// Creates a fork target for parallel execution using type-safe <see cref="JobRef"/> references.
    /// </summary>
    public static ForkTarget Fork(params JobRef[] targets) =>
        new(targets.Select(t => t.Name).ToArray());

    /// <summary>
    /// Creates a fork target with explicit cancellation mode using type-safe <see cref="JobRef"/> references.
    /// </summary>
    public static ForkTarget Fork(ForkMode mode, params JobRef[] targets) =>
        new(targets.Select(t => t.Name).ToArray(), mode);
}

/// <summary>
/// Fluent builder for constructing and running a typed workflow.
/// </summary>
/// <remarks>
/// <b>Thread safety:</b> <c>Workflow&lt;TState&gt;</c> is not thread-safe.
/// Construct and configure your workflow on a single thread before calling
/// <see cref="RunAsync"/> or <see cref="Build"/>.
/// The builder is frozen after <see cref="Build"/> — subsequent mutations throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class Workflow<TState>
{
    private readonly string _name;

    /// <summary>The workflow name passed to the constructor.</summary>
    public string Name => _name;

    private readonly Dictionary<string, JobDescriptor<TState>> _jobs = [];
    private readonly List<Connection> _connections = [];
    private readonly Dictionary<string, Func<TState, Task>?> _onEnterActions = [];
    private readonly Dictionary<string, Func<TState, Task>?> _onExitActions = [];
    private readonly Dictionary<string, Func<TState, Exception, Task>> _onFaultActions = [];
    private readonly Dictionary<string, TimeSpan> _timeouts = [];
    private readonly Dictionary<string, InterruptMode> _interrupts = [];
    private readonly HashSet<string> _inputJobs = [];
    private readonly List<JoinDescriptor<TState>> _joins = [];
    private Func<TState, string, Exception, Task>? _onError;
    private string? _entryJob;
    private IWorkflowRunner? _runner;
    private Usage.IUsageRecorder? _usageRecorder;
    private ICheckpointStore? _checkpointStore;
    private IWorkflowTracer? _tracer;
    private bool _storeCompletions;
    private Dictionary<string, string>? _metadata;
    private BudgetConfig? _budget;
    private WorkflowDefinition<TState>? _cachedDefinition;
    private bool _frozen;

    public Workflow(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException(
                $"Workflow '{_name}' is frozen after Build(). Create a new Workflow instance to make changes.");
    }

    public Workflow<TState> Job(string name, Func<TState, CancellationToken, Task<TState>> execute)
    {
        ThrowIfFrozen();
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

    /// <summary>
    /// Registers a delegate job and outputs a type-safe <see cref="JobRef"/> for use
    /// in connection methods like <see cref="Then(JobRef, JobRef)"/> and <see cref="Chain(JobRef[])"/>.
    /// </summary>
    public Workflow<TState> Job(string name, Func<TState, CancellationToken, Task<TState>> execute, out JobRef jobRef)
    {
        Job(name, execute);
        jobRef = new JobRef(name);
        return this;
    }

    public Workflow<TState> Job(string name, IJob<TState> job)
    {
        ThrowIfFrozen();
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

    /// <summary>
    /// Registers a named job and outputs a type-safe <see cref="JobRef"/> for use
    /// in connection methods like <see cref="Then(JobRef, JobRef)"/> and <see cref="Chain(JobRef[])"/>.
    /// </summary>
    public Workflow<TState> Job(string name, IJob<TState> job, out JobRef jobRef)
    {
        Job(name, job);
        jobRef = new JobRef(name);
        return this;
    }

    public Workflow<TState> Then(string from, string to)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        _connections.Add(new DirectConnection { From = from, To = to });
        return this;
    }

    public Workflow<TState> Then(string from, IRouter<TState> router)
    {
        ThrowIfFrozen();
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
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentNullException.ThrowIfNull(fork);

        _connections.Add(new ForkConnection { From = from, Targets = fork.Targets, Mode = fork.Mode });
        return this;
    }

    // ── JobRef overloads ────────────────────────────────────────────

    /// <summary>Connects two jobs using type-safe <see cref="JobRef"/> references.</summary>
    public Workflow<TState> Then(JobRef from, JobRef to) =>
        Then(from.Name, to.Name);

    /// <summary>Connects a job to a router using a type-safe <see cref="JobRef"/> reference.</summary>
    public Workflow<TState> Then(JobRef from, IRouter<TState> router) =>
        Then(from.Name, router);

    /// <summary>Connects a job to a fork target using a type-safe <see cref="JobRef"/> reference.</summary>
    public Workflow<TState> Then(JobRef from, ForkTarget fork) =>
        Then(from.Name, fork);

    /// <summary>
    /// Creates a loop that cycles from <paramref name="from"/> back to
    /// <paramref name="loopTarget"/> until <paramref name="until"/> returns <c>true</c>,
    /// then continues to <paramref name="exitTarget"/>.
    /// </summary>
    /// <param name="from">The evaluation job — its output state is tested each iteration.</param>
    /// <param name="loopTarget">The job to restart when the condition is not met.</param>
    /// <param name="exitTarget">The job to continue to when the loop exits (or <see cref="Workflow.End"/>).</param>
    /// <param name="until">Predicate evaluated after <paramref name="from"/> completes.</param>
    /// <param name="maxIterations">Safety cap. Default 10.</param>
    /// <example>
    /// <code>
    /// var workflow = new Workflow&lt;ReviewState&gt;("review-critique")
    ///     .Job("generate", generatorAgent)
    ///     .Job("critique", criticAgent)
    ///     .Then("generate", "critique")
    ///     .Loop("critique", loopTarget: "generate", exitTarget: Workflow.End,
    ///           until: s =&gt; s.Score &gt;= 0.9, maxIterations: 5);
    /// </code>
    /// </example>
    public Workflow<TState> Loop(
        string from,
        string loopTarget,
        string exitTarget,
        Func<TState, bool> until,
        int maxIterations = 10)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(loopTarget);
        ArgumentNullException.ThrowIfNull(until);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIterations, 1);

        if (string.IsNullOrWhiteSpace(exitTarget) && exitTarget != Workflow.EndMarker)
            ArgumentException.ThrowIfNullOrWhiteSpace(exitTarget);

        _connections.Add(new LoopConnection<TState>
        {
            From = from,
            LoopTarget = loopTarget,
            ExitTarget = exitTarget,
            Until = until,
            MaxIterations = maxIterations
        });
        return this;
    }

    /// <summary>
    /// Creates a loop using type-safe <see cref="JobRef"/> references.
    /// </summary>
    public Workflow<TState> Loop(
        JobRef from,
        JobRef loopTarget,
        JobRef exitTarget,
        Func<TState, bool> until,
        int maxIterations = 10) =>
        Loop(from.Name, loopTarget.Name, exitTarget.Name, until, maxIterations);

    /// <summary>
    /// Defines a fan-in point where parallel branches converge. The <paramref name="merge"/>
    /// function reconciles the final state from each branch into a single state.
    /// For correct results, <typeparamref name="TState"/> should be immutable (e.g. a record).
    /// </summary>
    public Workflow<TState> Join(string[] sources, string target, Func<TState[], TState> merge)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(merge);

        if (sources.Length < 2)
            throw new ArgumentException("Join requires at least two sources.", nameof(sources));

        _joins.Add(new JoinDescriptor<TState>(sources, target, merge));
        return this;
    }

    /// <summary>
    /// Defines a fan-in point using type-safe <see cref="JobRef"/> references.
    /// </summary>
    public Workflow<TState> Join(JobRef[] sources, JobRef target, Func<TState[], TState> merge) =>
        Join(sources.Select(s => s.Name).ToArray(), target.Name, merge);

    /// <summary>
    /// Defines a fan-in point whose <paramref name="merge"/> callback also receives the outcome of
    /// every branch — including any that failed — so it can decide what a partial fork result means.
    /// </summary>
    /// <remarks>
    /// Use this overload with <see cref="ForkMode.BestEffort"/>, where a branch can be dropped from
    /// the merge: <see cref="JoinContext{TState}.HasFailures"/> tells the callback whether
    /// <see cref="JoinContext{TState}.States"/> is complete, letting it substitute a default,
    /// accept the partial result, or throw. The <see cref="Func{T, TResult}"/> overload cannot see
    /// this and treats a short state list as normal.
    /// </remarks>
    public Workflow<TState> Join(string[] sources, string target, Func<JoinContext<TState>, TState> merge)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(merge);

        if (sources.Length < 2)
            throw new ArgumentException("Join requires at least two sources.", nameof(sources));

        _joins.Add(new JoinDescriptor<TState>(sources, target, merge));
        return this;
    }

    /// <summary>
    /// Defines an outcome-aware fan-in point using type-safe <see cref="JobRef"/> references.
    /// </summary>
    public Workflow<TState> Join(JobRef[] sources, JobRef target, Func<JoinContext<TState>, TState> merge) =>
        Join(sources.Select(s => s.Name).ToArray(), target.Name, merge);

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
    /// Registers a nested workflow as a job and outputs a type-safe <see cref="JobRef"/>.
    /// </summary>
    public Workflow<TState> SubFlow<TChild>(
        string name,
        Workflow<TChild> innerWorkflow,
        Func<TState, TChild> mapIn,
        Func<TState, TChild, TState> mapOut,
        out JobRef jobRef,
        int maxDepth = 5)
    {
        SubFlow(name, innerWorkflow, mapIn, mapOut, maxDepth);
        jobRef = new JobRef(name);
        return this;
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

    /// <summary>
    /// Chains one or more jobs together in a linear sequence using type-safe <see cref="JobRef"/> references.
    /// The last job in the chain is implicitly terminal — no explicit <see cref="Workflow.EndRef"/> needed.
    /// </summary>
    public Workflow<TState> Chain(params JobRef[] jobRefs) =>
        Chain(jobRefs.Select(r => r.Name).ToArray());

    public Workflow<TState> OnEnter(string jobName, Func<TState, Task> action)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        _onEnterActions[jobName] = action;
        return this;
    }

    /// <summary>Registers an on-enter lifecycle action using a type-safe <see cref="JobRef"/>.</summary>
    public Workflow<TState> OnEnter(JobRef jobRef, Func<TState, Task> action) =>
        OnEnter(jobRef.Name, action);

    public Workflow<TState> OnExit(string jobName, Func<TState, Task> action)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        _onExitActions[jobName] = action;
        return this;
    }

    /// <summary>Registers an on-exit lifecycle action using a type-safe <see cref="JobRef"/>.</summary>
    public Workflow<TState> OnExit(JobRef jobRef, Func<TState, Task> action) =>
        OnExit(jobRef.Name, action);

    /// <summary>
    /// Registers a per-job error handler invoked when the specified job throws.
    /// The handler runs before the workflow terminates and receives the current state
    /// and the exception. Use for cleanup, alerting, or logging.
    /// </summary>
    /// <param name="jobName">The job to attach the fault handler to.</param>
    /// <param name="handler">Async callback receiving the state and the thrown exception.</param>
    public Workflow<TState> OnFault(string jobName, Func<TState, Exception, Task> handler)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(handler);
        _onFaultActions[jobName] = handler;
        return this;
    }

    /// <summary>Registers a per-job fault handler using a type-safe <see cref="JobRef"/>.</summary>
    public Workflow<TState> OnFault(JobRef jobRef, Func<TState, Exception, Task> handler) =>
        OnFault(jobRef.Name, handler);

    /// <summary>
    /// Registers a workflow-level error handler invoked when <em>any</em> job throws
    /// an unhandled exception. The handler runs after any per-job <see cref="OnFault(string, Func{TState, Exception, Task})"/>
    /// handler and before the workflow terminates.
    /// </summary>
    /// <param name="handler">
    /// Async callback receiving the current state, the faulting job name, and the exception.
    /// </param>
    public Workflow<TState> OnError(Func<TState, string, Exception, Task> handler)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(handler);
        _onError = handler;
        return this;
    }

    public Workflow<TState> Timeout(string jobName, TimeSpan timeout)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeouts[jobName] = timeout;
        return this;
    }

    /// <summary>Sets a job timeout using a type-safe <see cref="JobRef"/>.</summary>
    public Workflow<TState> Timeout(JobRef jobRef, TimeSpan timeout) =>
        Timeout(jobRef.Name, timeout);

    /// <summary>
    /// Pauses workflow execution <em>before</em> the specified job runs.
    /// The execution is checkpointed with <see cref="ExecutionStatus.Interrupted"/> status
    /// and can be resumed via <see cref="ResumeAsync(string, CancellationToken)"/>.
    /// Requires <see cref="UseCheckpointing"/> to be configured.
    /// </summary>
    public Workflow<TState> InterruptBefore(string jobName)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        _interrupts[jobName] = InterruptMode.Before;
        return this;
    }

    /// <summary>Pauses workflow execution <em>before</em> the specified job using a type-safe <see cref="JobRef"/>.</summary>
    public Workflow<TState> InterruptBefore(JobRef jobRef) =>
        InterruptBefore(jobRef.Name);

    /// <summary>
    /// Pauses workflow execution <em>after</em> the specified job completes.
    /// The execution is checkpointed with <see cref="ExecutionStatus.Interrupted"/> status
    /// and can be resumed via <see cref="ResumeAsync(string, CancellationToken)"/>.
    /// Requires <see cref="UseCheckpointing"/> to be configured.
    /// </summary>
    public Workflow<TState> InterruptAfter(string jobName)
    {
        ThrowIfFrozen();
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        _interrupts[jobName] = InterruptMode.After;
        return this;
    }

    /// <summary>Pauses workflow execution <em>after</em> the specified job using a type-safe <see cref="JobRef"/>.</summary>
    public Workflow<TState> InterruptAfter(JobRef jobRef) =>
        InterruptAfter(jobRef.Name);

    /// <summary>
    /// Marks <paramref name="node"/> as an input-collecting turn: pauses execution before the
    /// job (exactly like <see cref="InterruptBefore(string)"/>) and records it in
    /// <see cref="WorkflowDefinition{TState}.InputJobs"/>, so a host can tell a turn awaiting a
    /// free-text reply apart from an approval gate and resume with
    /// <see cref="ResumeAsync(string, Func{TState, TState}, CancellationToken)"/> accordingly.
    /// Input is always free text — there is no typed input contract. Requires
    /// <see cref="UseCheckpointing"/> to be configured.
    /// </summary>
    public Workflow<TState> AwaitInput(string node)
    {
        InterruptBefore(node);
        _inputJobs.Add(node);
        return this;
    }

    /// <summary>Marks a job as an input-collecting turn using a type-safe <see cref="JobRef"/>.</summary>
    public Workflow<TState> AwaitInput(JobRef node) =>
        AwaitInput(node.Name);

    public Workflow<TState> UseRunner(IWorkflowRunner runner)
    {
        ThrowIfFrozen();
        _runner = runner;
        return this;
    }

    public Workflow<TState> UseCheckpointing(ICheckpointStore store)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(store);
        _checkpointStore = store;
        return this;
    }

    /// <summary>
    /// Supplies the recorder that usage and spend accumulate into. Required when
    /// <see cref="BudgetConfig.PeriodCostLimit"/> is set.
    /// </summary>
    /// <remarks>
    /// Without one, an execution uses an in-memory recorder scoped to that run — correct for
    /// <see cref="BudgetConfig.MaxCost"/>, and wrong for a period ceiling, which must outlive the
    /// process. Use <see cref="Budget.FileUsageRecorder"/> for the in-box durable option.
    /// </remarks>
    public Workflow<TState> UseUsageRecorder(Usage.IUsageRecorder recorder)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(recorder);

        _usageRecorder = recorder;
        return this;
    }

    public Workflow<TState> UseTracing(IWorkflowTracer tracer)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(tracer);
        _tracer = tracer;
        return this;
    }

    /// <summary>
    /// Controls whether LLM completions are stored in the provider's platform logs
    /// (e.g. <see href="https://platform.openai.com/logs"/>). Default is <c>false</c>.
    /// </summary>
    public Workflow<TState> StoreCompletions(bool enabled)
    {
        ThrowIfFrozen();
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
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
        return this;
    }

    /// <summary>
    /// Sets a cost budget for the workflow, using model-specific rates from
    /// <see cref="Agents.Routing.ModelProfile"/> for per-call costing. Ideal for multi-model
    /// workflows where each model has different cost rates (including zero-cost local models).
    /// If cumulative estimated cost exceeds <paramref name="maxCost"/>, the workflow
    /// terminates with <see cref="ExecutionStatus.BudgetExceeded"/>.
    /// </summary>
    /// <param name="maxCost">Maximum allowed estimated cost.</param>
    /// <example>
    /// <code>
    /// // Models provide their own cost rates via ModelProfile:
    /// var router = new CapabilityModelRouter()
    ///     .AddModel(new ModelProfile
    ///     {
    ///         Name = "gpt-4.1-mini", Model = miniModel,
    ///         CostPer1KInputTokens = 0.0004m, CostPer1KOutputTokens = 0.0016m, ...
    ///     })
    ///     .AddModel(new ModelProfile
    ///     {
    ///         Name = "llama3.2:3b", Model = ollamaModel, // zero cost by default
    ///         ...
    ///     });
    ///
    /// workflow.WithBudget(maxCost: 0.50m);
    /// </code>
    /// </example>
    public Workflow<TState> WithBudget(decimal maxCost)
    {
        ThrowIfFrozen();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxCost, 0);

        _budget = new BudgetConfig { MaxCost = maxCost };
        return this;
    }

    /// <summary>
    /// Sets a cost budget with fallback rates declared the way providers publish them —
    /// per <b>million</b> tokens.
    /// </summary>
    /// <param name="maxCost">Spend ceiling, in the same currency as the rates.</param>
    /// <param name="inputPerMillion">Cost per 1,000,000 input tokens, e.g. <c>0.15m</c>.</param>
    /// <param name="outputPerMillion">Cost per 1,000,000 output tokens, e.g. <c>0.60m</c>.</param>
    /// <example>
    /// <code>
    /// workflow.WithBudgetPerMillion(maxCost: 25m, inputPerMillion: 0.15m, outputPerMillion: 0.60m);
    /// </code>
    /// </example>
    public Workflow<TState> WithBudgetPerMillion(
        decimal maxCost, decimal inputPerMillion, decimal outputPerMillion)
    {
        ThrowIfFrozen();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxCost, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(inputPerMillion);
        ArgumentOutOfRangeException.ThrowIfNegative(outputPerMillion);

        _budget = BudgetConfig.FromPerMillion(maxCost, inputPerMillion, outputPerMillion);
        return this;
    }

    /// <summary>
    /// Sets a fully-specified cost budget — the only overload that can reach
    /// <see cref="BudgetConfig.WarnAtCost"/> and <see cref="BudgetConfig.Mode"/>.
    /// </summary>
    /// <param name="budget">The budget configuration.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="BudgetConfig.MaxCost"/> is not positive, or
    /// <see cref="BudgetConfig.WarnAtCost"/> is not below it — a warning threshold at or above
    /// the limit could never fire before the run stopped, so it is a configuration mistake
    /// rather than a no-op.
    /// </exception>
    public Workflow<TState> WithBudget(BudgetConfig budget)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget.MaxCost, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(budget.CostPer1KInputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(budget.CostPer1KOutputTokens);

        if (budget.WarnAtCost is { } warnAt)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warnAt);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(warnAt, budget.MaxCost);
        }

        _budget = budget;
        return this;
    }

    /// <summary>
    /// Sets a cost budget for the workflow with flat fallback cost rates.
    /// These rates are used when model-specific rates are not available
    /// (e.g. direct <c>IAgentModel</c> usage without
    /// <see cref="Agents.Routing.ModelProfile"/>). If cumulative estimated cost exceeds
    /// <paramref name="maxCost"/>, the workflow terminates with
    /// <see cref="ExecutionStatus.BudgetExceeded"/>.
    /// </summary>
    /// <param name="maxCost">Maximum allowed estimated cost.</param>
    /// <param name="costPer1KInputTokens">Fallback cost per 1,000 input tokens.</param>
    /// <param name="costPer1KOutputTokens">Fallback cost per 1,000 output tokens.</param>
    public Workflow<TState> WithBudget(
        decimal maxCost,
        decimal costPer1KInputTokens,
        decimal costPer1KOutputTokens)
    {
        ThrowIfFrozen();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxCost, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(costPer1KInputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(costPer1KOutputTokens);

        _budget = new BudgetConfig
        {
            MaxCost = maxCost,
            CostPer1KInputTokens = costPer1KInputTokens,
            CostPer1KOutputTokens = costPer1KOutputTokens
        };
        return this;
    }

    /// <summary>
    /// Eagerly validates the workflow definition, throwing on any configuration errors.
    /// Call after defining all jobs and connections to fail fast at startup rather than
    /// deferring validation to the first <see cref="RunAsync"/> call.
    /// </summary>
    /// <returns>The builder, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">The workflow definition is invalid.</exception>
    public Workflow<TState> Validate()
    {
        Build();
        return this;
    }

    public WorkflowDefinition<TState> Build()
    {
        if (_cachedDefinition is not null)
            return _cachedDefinition;

        if (_entryJob is null)
            throw new InvalidOperationException("Workflow must have at least one job.");

        // 4.2: Fail at Build() when a budget is configured without any cost-rate source.
        // A budget is only actionable when either (a) flat fallback rates are provided, or
        // (b) at least one job uses a profile-aware router that can supply per-call rates.
        if (_budget is not null && !_budget.HasFallbackRates)
        {
            var hasProfileAware = _jobs.Values.Any(d => d.Job is IProfileAwareJob { HasProfileAwareModel: true });
            if (!hasProfileAware)
                throw new InvalidOperationException(
                    $"Workflow '{_name}' has a budget but no cost rates are configured. " +
                    "Either supply fallback rates via WithBudgetPerMillion(maxCost, inputPerMillion, outputPerMillion) " +
                    "— the unit providers publish — or route model calls through a CapabilityModelRouter " +
                    "so per-call rates are available.");
        }

        // D15: fail closed. A period ceiling backed by the default in-memory recorder would
        // reset on every process start, so a crash-loop would re-spend the month indefinitely
        // with nothing reporting a problem. Refuse the configuration instead.
        if (_budget?.PeriodCostLimit is not null && _usageRecorder is null)
            throw new InvalidOperationException(
                $"Workflow '{_name}' sets a period cost limit but has no usage recorder. " +
                "A period ceiling accumulates across runs, so it needs storage that outlives the " +
                "process — call UseUsageRecorder(new FileUsageRecorder(...)) on the workflow builder. " +
                "Without one the limit would silently reset on every restart.");

        ApplyLifecycleActions();

        _cachedDefinition = new WorkflowDefinition<TState>(_name, _jobs, _connections, _entryJob, _metadata, _joins, _budget, _onError, _inputJobs);
        _frozen = true;
        return _cachedDefinition;
    }

    public async Task<WorkflowExecution<TState>> RunAsync(
        TState initialState,
        CancellationToken ct = default)
    {
        var definition = Build();
        var runner = _runner ?? new WorkflowRunner(_checkpointStore, tracer: _tracer, storeCompletions: _storeCompletions, usageRecorder: _usageRecorder);
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
        var runner = _runner ?? new WorkflowRunner(_checkpointStore, tracer: _tracer, storeCompletions: _storeCompletions, usageRecorder: _usageRecorder);
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
        var runner = _runner ?? new WorkflowRunner(_checkpointStore, tracer: _tracer, storeCompletions: _storeCompletions, usageRecorder: _usageRecorder);
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
        var runner = _runner ?? new WorkflowRunner(_checkpointStore, tracer: _tracer, storeCompletions: _storeCompletions, usageRecorder: _usageRecorder);
        return await runner.ResumeAsync(definition, checkpoint, stateTransform, ct);
    }

    private void ApplyLifecycleActions()
    {
        foreach (var (jobName, action) in _onEnterActions)
        {
            if (!_jobs.TryGetValue(jobName, out var descriptor))
                throw new InvalidOperationException($"OnEnter references undefined job '{jobName}'.");
            _jobs[jobName] = descriptor with { OnEnter = action };
        }

        foreach (var (jobName, action) in _onExitActions)
        {
            if (!_jobs.TryGetValue(jobName, out var descriptor))
                throw new InvalidOperationException($"OnExit references undefined job '{jobName}'.");
            _jobs[jobName] = descriptor with { OnExit = action };
        }

        foreach (var (jobName, handler) in _onFaultActions)
        {
            if (!_jobs.TryGetValue(jobName, out var descriptor))
                throw new InvalidOperationException($"OnFault references undefined job '{jobName}'.");
            _jobs[jobName] = descriptor with { OnFault = handler };
        }

        foreach (var (jobName, timeout) in _timeouts)
        {
            if (!_jobs.TryGetValue(jobName, out var descriptor))
                throw new InvalidOperationException($"Timeout references undefined job '{jobName}'.");
            _jobs[jobName] = descriptor with { Timeout = timeout };
        }

        foreach (var (jobName, mode) in _interrupts)
        {
            if (!_jobs.TryGetValue(jobName, out var descriptor))
                throw new InvalidOperationException($"Interrupt references undefined job '{jobName}'.");
            _jobs[jobName] = descriptor with { Interrupt = mode };
        }

        foreach (var (_, descriptor) in _jobs)
        {
            if (descriptor.Job is ISubFlowConfiguration subflow)
                subflow.ConfigureInfrastructure(_checkpointStore, _tracer, _storeCompletions);
        }
    }
}
