using Ananke.Design.Dsl;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Routing;

namespace Ananke.Design;

/// <summary>
/// A parsed workflow topology that can be incrementally bound to job implementations,
/// merge functions, and routers before producing a runnable <see cref="Workflow{TState}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Created via <see cref="WorkflowScaffold.Parse{TState}(string, string)"/> or
/// <see cref="WorkflowScaffold.Parse{TState}(string, IEnumerable{string})"/>.
/// The DSL defines the graph structure; code supplies behavior via <c>Bind</c> methods.
/// </para>
/// <para><b>Thread safety:</b> not thread-safe. Configure and call <see cref="Build"/> on a single thread.</para>
/// </remarks>
/// <example>
/// <code>
/// var scaffold = WorkflowScaffold.Parse&lt;MyState&gt;("etl-pipeline", """
///     plan -> fork(fetch_a, fetch_b)
///     fetch_a -> transform_a
///     fetch_b -> transform_b
///     join(transform_a, transform_b) -> combine
///     combine -> End
///     """);
///
/// var workflow = scaffold
///     .Bind("plan", async (state, ct) => state with { Step = "planned" })
///     .Bind("fetch_a", fetchAJob)
///     .Bind("fetch_b", fetchBJob)
///     .Bind("transform_a", async (state, ct) => state)
///     .Bind("transform_b", async (state, ct) => state)
///     .Bind("combine", async (state, ct) => state)
///     .BindMerge("combine", branches => branches[0])
///     .Build();
/// </code>
/// </example>
public sealed class WorkflowScaffold<TState>
{
    private readonly string _name;
    private readonly List<ConnectionLine> _connections;
    private readonly Dictionary<string, ToolDirective> _toolDeclarations;
    private readonly Dictionary<string, JobToolDirective> _jobToolDeclarations;
    private readonly HashSet<string> _jobNames;
    private readonly HashSet<string> _subFlowNames;
    private readonly HashSet<string> _interruptJobs;
    private readonly HashSet<string> _askJobs;
    private readonly Dictionary<string, IJob<TState>> _boundJobs = [];
    private readonly Dictionary<string, Func<TState, CancellationToken, Task<TState>>> _boundDelegates = [];
    private readonly Dictionary<string, Func<TState[], TState>> _mergeFunctions = [];
    private readonly Dictionary<string, IRouter<TState>> _routers = [];
    private readonly Dictionary<string, Action<Workflow<TState>>> _subFlowBindings = [];
    private readonly Dictionary<string, Func<TState, bool>> _loopConditions = [];

    internal WorkflowScaffold(string name, List<ConnectionLine> connections)
    {
        _name = name;
        _connections = connections;
        _toolDeclarations = DiscoverTools(connections);
        _jobToolDeclarations = DiscoverJobToolDeclarations(connections);
        _jobNames = DiscoverJobNames(connections);

        if (_jobNames.Count == 0)
            throw new InvalidOperationException("DSL produced no job names. At least one connection is required.");

        _subFlowNames = connections.OfType<ConnectionLine.SubFlow>()
            .Select(sf => sf.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _interruptJobs = connections.OfType<ConnectionLine.Interrupt>()
            .Select(i => i.JobName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknownSubFlows = _subFlowNames.Where(n => !_jobNames.Contains(n)).ToList();
        if (unknownSubFlows.Count > 0)
            throw new InvalidOperationException(
                $"SubFlow directive(s) reference unknown job(s): {string.Join(", ", unknownSubFlows)}. " +
                "Each subflow name must appear in a connection.");

        var unknownInterrupts = _interruptJobs.Where(n => !_jobNames.Contains(n)).ToList();
        if (unknownInterrupts.Count > 0)
            throw new InvalidOperationException(
                $"Interrupt directive(s) reference unknown job(s): {string.Join(", ", unknownInterrupts)}. " +
                "Each interrupt target must appear in a connection.");

        _askJobs = connections.OfType<ConnectionLine.Ask>()
            .Select(a => a.JobName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknownAsks = _askJobs.Where(n => !_jobNames.Contains(n)).ToList();
        if (unknownAsks.Count > 0)
            throw new InvalidOperationException(
                $"Ask directive(s) reference unknown job(s): {string.Join(", ", unknownAsks)}. " +
                "Each ask target must appear in a connection.");

        var loops = connections.OfType<ConnectionLine.Loop>().ToList();

        var unknownLoopSources = loops.Select(l => l.From).Where(n => !_jobNames.Contains(n)).ToList();
        if (unknownLoopSources.Count > 0)
            throw new InvalidOperationException(
                $"Loop directive(s) reference unknown source job(s): {string.Join(", ", unknownLoopSources)}. " +
                "Each loop source must appear in a connection.");

        var unknownLoopTargets = loops.Select(l => l.LoopTarget).Where(n => !_jobNames.Contains(n)).ToList();
        if (unknownLoopTargets.Count > 0)
            throw new InvalidOperationException(
                $"Loop directive(s) reference unknown loop target(s): {string.Join(", ", unknownLoopTargets)}. " +
                "Each loop target must appear in a connection.");

        var unknownLoopExits = loops.Select(l => l.ExitTarget)
            .Where(t => !string.Equals(t, "End", StringComparison.OrdinalIgnoreCase) && !_jobNames.Contains(t))
            .ToList();
        if (unknownLoopExits.Count > 0)
            throw new InvalidOperationException(
                $"Loop directive(s) reference unknown exit target(s): {string.Join(", ", unknownLoopExits)}. " +
                "Each loop exit target must appear in a connection, or be 'End'.");
    }

    /// <summary>
    /// All job names discovered from the parsed topology.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// All job names discovered from the parsed topology.
    /// </summary>
    public IReadOnlySet<string> JobNames => _jobNames;

    /// <summary>
    /// Manifest-style tool declarations discovered from DSL <c>tool(...)</c> directives.
    /// </summary>
    public IReadOnlyDictionary<string, ToolDirective> ToolDeclarations => _toolDeclarations;

    /// <summary>
    /// Per-job tool usage directives discovered from DSL <c>use(...)</c> lines.
    /// </summary>
    public IReadOnlyDictionary<string, JobToolDirective> JobToolDeclarations => _jobToolDeclarations;

    /// <summary>
    /// Returns only topology DSL lines, excluding non-topology directives such as <c>tool(...)</c> and <c>use(...)</c>.
    /// </summary>
    public IReadOnlyList<string> GetTopologyDsl() =>
        _connections
            .Where(static c => c is not ConnectionLine.Tool && c is not ConnectionLine.Use)
            .Select(ToDslLine)
            .ToList();

    /// <summary>
    /// Job names that have not yet been bound to an implementation.
    /// Excludes jobs declared as subflows (use <see cref="BindSubFlow{TChild}"/> for those).
    /// </summary>
    public IReadOnlySet<string> UnboundJobs =>
        new HashSet<string>(_jobNames.Where(n =>
            !_boundJobs.ContainsKey(n) &&
            !_boundDelegates.ContainsKey(n) &&
            !_subFlowNames.Contains(n)));

    /// <summary>
    /// Join targets that have not yet been bound to a merge function.
    /// </summary>
    public IReadOnlySet<string> UnboundMerges
    {
        get
        {
            var joinTargets = _connections
                .OfType<ConnectionLine.Join>()
                .Select(j => j.Target)
                .Where(t => !string.Equals(t, "End", StringComparison.OrdinalIgnoreCase))
                .ToHashSet();

            joinTargets.ExceptWith(_mergeFunctions.Keys);
            return joinTargets;
        }
    }

    /// <summary>
    /// Router source jobs that have not yet been bound to an <see cref="IRouter{TState}"/>.
    /// </summary>
    public IReadOnlySet<string> UnboundRouters
    {
        get
        {
            var routerJobs = _connections
                .OfType<ConnectionLine.Router>()
                .Select(r => r.From)
                .ToHashSet();

            routerJobs.ExceptWith(_routers.Keys);
            return routerJobs;
        }
    }

    /// <summary>
    /// SubFlow names that have not yet been bound via <see cref="BindSubFlow{TChild}"/>.
    /// </summary>
    public IReadOnlySet<string> UnboundSubFlows
    {
        get
        {
            var unbound = new HashSet<string>(_subFlowNames, StringComparer.OrdinalIgnoreCase);
            unbound.ExceptWith(_subFlowBindings.Keys);
            return unbound;
        }
    }

    /// <summary>
    /// Loop source jobs that have not yet been bound to an <c>until</c> condition via
    /// <see cref="BindLoopCondition"/>.
    /// </summary>
    public IReadOnlySet<string> UnboundLoops
    {
        get
        {
            var loopSources = _connections
                .OfType<ConnectionLine.Loop>()
                .Select(l => l.From)
                .ToHashSet();

            loopSources.ExceptWith(_loopConditions.Keys);
            return loopSources;
        }
    }

    /// <summary>
    /// Binds a job name to a delegate implementation.
    /// </summary>
    public WorkflowScaffold<TState> Bind(string jobName, Func<TState, CancellationToken, Task<TState>> execute)
    {
        ValidateJobName(jobName);
        _boundDelegates[jobName] = execute;
        return this;
    }

    /// <summary>
    /// Binds a job name to an <see cref="IJob{TState}"/> implementation.
    /// </summary>
    public WorkflowScaffold<TState> Bind(string jobName, IJob<TState> job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ValidateJobName(jobName);
        _boundJobs[jobName] = job;
        return this;
    }

    /// <summary>
    /// Binds a merge function for a join target. Required for each <c>join(...) -&gt; target</c> in the DSL.
    /// </summary>
    public WorkflowScaffold<TState> BindMerge(string joinTarget, Func<TState[], TState> merge)
    {
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentException.ThrowIfNullOrWhiteSpace(joinTarget);

        var isJoinTarget = _connections.OfType<ConnectionLine.Join>().Any(j =>
            string.Equals(j.Target, joinTarget, StringComparison.OrdinalIgnoreCase));

        if (!isJoinTarget)
            throw new InvalidOperationException(
                $"'{joinTarget}' is not a join target in the DSL. " +
                $"Join targets: {string.Join(", ", _connections.OfType<ConnectionLine.Join>().Select(j => j.Target))}");

        _mergeFunctions[joinTarget] = merge;
        return this;
    }

    /// <summary>
    /// Binds a router implementation for a decision point declared with <c>router(...)</c> in the DSL.
    /// </summary>
    public WorkflowScaffold<TState> BindRouter(string jobName, IRouter<TState> router)
    {
        ArgumentNullException.ThrowIfNull(router);
        ValidateJobName(jobName);

        var isRouterJob = _connections.OfType<ConnectionLine.Router>().Any(r =>
            string.Equals(r.From, jobName, StringComparison.OrdinalIgnoreCase));

        if (!isRouterJob)
            throw new InvalidOperationException(
                $"'{jobName}' is not a router job in the DSL.");

        _routers[jobName] = router;
        return this;
    }

    /// <summary>
    /// Binds the <c>until</c> condition for a loop declared with <c>loop(target, exit: x)</c> in
    /// the DSL. Required for each loop source before <see cref="Build"/>.
    /// </summary>
    /// <param name="from">The loop's source job name (the <c>a</c> in <c>a -&gt; loop(...)</c>).</param>
    /// <param name="until">Evaluated after <paramref name="from"/> completes; <c>true</c> exits the loop.</param>
    public WorkflowScaffold<TState> BindLoopCondition(string from, Func<TState, bool> until)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentNullException.ThrowIfNull(until);

        var isLoopSource = _connections.OfType<ConnectionLine.Loop>().Any(l =>
            string.Equals(l.From, from, StringComparison.OrdinalIgnoreCase));

        if (!isLoopSource)
            throw new InvalidOperationException(
                $"'{from}' is not a loop source in the DSL.");

        _loopConditions[from] = until;
        return this;
    }

    /// <summary>
    /// Binds a nested workflow for a job declared with <c>subflow(name)</c> in the DSL.
    /// </summary>
    /// <typeparam name="TChild">State type of the inner workflow.</typeparam>
    /// <param name="name">Job name matching a <c>subflow(name)</c> directive.</param>
    /// <param name="innerWorkflow">The nested workflow to execute.</param>
    /// <param name="mapIn">Transforms the parent state into the child workflow's initial state.</param>
    /// <param name="mapOut">Merges the child result back into the parent state.</param>
    /// <param name="maxDepth">Maximum nesting depth (default 5).</param>
    public WorkflowScaffold<TState> BindSubFlow<TChild>(
        string name,
        Workflow<TChild> innerWorkflow,
        Func<TState, TChild> mapIn,
        Func<TState, TChild, TState> mapOut,
        int maxDepth = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(innerWorkflow);
        ArgumentNullException.ThrowIfNull(mapIn);
        ArgumentNullException.ThrowIfNull(mapOut);

        if (!_subFlowNames.Contains(name))
            throw new InvalidOperationException(
                $"'{name}' is not declared as a subflow in the DSL. " +
                $"Add 'subflow({name})' to the DSL.");

        _subFlowBindings[name] = wf => wf.SubFlow(name, innerWorkflow, mapIn, mapOut, maxDepth);
        return this;
    }

    /// <summary>
    /// Builds the fully bound workflow. Throws if any jobs, merges, or routers are unbound.
    /// </summary>
    public Workflow<TState> Build()
    {
        ValidateAllBound();

        var workflow = new Workflow<TState>(_name);

        // Register jobs in discovery order (first seen = entry job)
        foreach (var name in DiscoverOrderedJobNames(_connections))
        {
            if (_subFlowBindings.TryGetValue(name, out var applySubFlow))
            {
                applySubFlow(workflow);
            }
            else if (_boundJobs.TryGetValue(name, out var job))
                workflow.Job(name, job);
            else if (_boundDelegates.TryGetValue(name, out var execute))
                workflow.Job(name, execute);
        }

        // Apply connections
        foreach (var conn in _connections)
        {
            switch (conn)
            {
                case ConnectionLine.Direct d:
                    var to = string.Equals(d.To, "End", StringComparison.OrdinalIgnoreCase)
                        ? Workflow.End : d.To;
                    workflow.Then(d.From, to);
                    break;

                case ConnectionLine.Fork f:
                    var mode = ParseForkMode(f.Mode);
                    workflow.Then(f.From, mode.HasValue
                        ? Workflow.Fork(mode.Value, f.Targets)
                        : Workflow.Fork(f.Targets));
                    break;

                case ConnectionLine.Join j:
                    var target = string.Equals(j.Target, "End", StringComparison.OrdinalIgnoreCase)
                        ? Workflow.End : j.Target;
                    workflow.Join(j.Sources, target, _mergeFunctions[j.Target]);
                    break;

                case ConnectionLine.Router r:
                    workflow.Then(r.From, _routers[r.From]);
                    break;

                case ConnectionLine.Loop loop:
                    var loopExit = string.Equals(loop.ExitTarget, "End", StringComparison.OrdinalIgnoreCase)
                        ? Workflow.End : loop.ExitTarget;
                    workflow.Loop(
                        loop.From, loop.LoopTarget, loopExit,
                        _loopConditions[loop.From], loop.MaxIterations ?? 10);
                    break;

                case ConnectionLine.SubFlow:
                case ConnectionLine.Interrupt:
                case ConnectionLine.Ask:
                case ConnectionLine.Tool:
                case ConnectionLine.Use:
                    break; // node annotations — handled separately
            }
        }

        // Apply interrupt directives
        foreach (var job in _interruptJobs)
            workflow.InterruptBefore(job);

        // Apply ask (input-turn) directives
        foreach (var job in _askJobs)
            workflow.AwaitInput(job);

        return workflow;
    }

    private void ValidateJobName(string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        if (!_jobNames.Contains(jobName))
            throw new InvalidOperationException(
                $"Job '{jobName}' is not declared in the DSL. " +
                $"Known jobs: {string.Join(", ", _jobNames)}");
    }

    private void ValidateAllBound()
    {
        var unbound = UnboundJobs;
        if (unbound.Count > 0)
            throw new InvalidOperationException(
                $"Unbound job(s): {string.Join(", ", unbound)}. " +
                "Call Bind() for each job before building.");

        var unboundMerges = UnboundMerges;
        if (unboundMerges.Count > 0)
            throw new InvalidOperationException(
                $"Unbound merge(s) for join target(s): {string.Join(", ", unboundMerges)}. " +
                "Call BindMerge() for each join target before building.");

        var unboundRouters = UnboundRouters;
        if (unboundRouters.Count > 0)
            throw new InvalidOperationException(
                $"Unbound router(s): {string.Join(", ", unboundRouters)}. " +
                "Call BindRouter() for each router job before building.");

        var unboundSubFlows = UnboundSubFlows;
        if (unboundSubFlows.Count > 0)
            throw new InvalidOperationException(
                $"Unbound subflow(s): {string.Join(", ", unboundSubFlows)}. " +
                "Call BindSubFlow() for each subflow before building.");

        var unboundLoops = UnboundLoops;
        if (unboundLoops.Count > 0)
            throw new InvalidOperationException(
                $"Unbound loop(s): {string.Join(", ", unboundLoops)}. " +
                "Call BindLoopCondition() for each loop before building.");
    }

    private static ForkMode? ParseForkMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        null => null,
        "fail-fast" or "failfast" => ForkMode.FailFast,
        "best-effort" or "besteffort" => ForkMode.BestEffort,
        _ => throw new FormatException($"Unknown fork mode: '{mode}'. Use 'fail-fast' or 'best-effort'.")
    };

    /// <summary>
    /// Discovers all unique job names referenced in the DSL, preserving first-seen order.
    /// "End" is excluded — it's a terminal marker, not a job.
    /// </summary>
    private static List<string> DiscoverOrderedJobNames(List<ConnectionLine> connections)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void TryAdd(string name)
        {
            if (!string.Equals(name, "End", StringComparison.OrdinalIgnoreCase) && seen.Add(name))
                ordered.Add(name);
        }

        foreach (var conn in connections)
        {
            switch (conn)
            {
                case ConnectionLine.Direct d:
                    TryAdd(d.From);
                    TryAdd(d.To);
                    break;
                case ConnectionLine.Fork f:
                    TryAdd(f.From);
                    foreach (var t in f.Targets) TryAdd(t);
                    break;
                case ConnectionLine.Join j:
                    foreach (var s in j.Sources) TryAdd(s);
                    TryAdd(j.Target);
                    break;
                case ConnectionLine.Router r:
                    TryAdd(r.From);
                    foreach (var o in r.Options) TryAdd(o);
                    break;

                case ConnectionLine.Loop:
                case ConnectionLine.SubFlow:
                case ConnectionLine.Interrupt:
                case ConnectionLine.Ask:
                case ConnectionLine.Tool:
                case ConnectionLine.Use:
                    break; // node annotations — don't introduce new names
            }
        }

        return ordered;
    }

    private static HashSet<string> DiscoverJobNames(List<ConnectionLine> connections) =>
        [.. DiscoverOrderedJobNames(connections)];

    private static string ToDslLine(ConnectionLine connection) => connection switch
    {
        ConnectionLine.Direct d => $"{d.From} -> {d.To}",
        ConnectionLine.Fork f => f.Mode is null
            ? $"{f.From} -> fork({string.Join(", ", f.Targets)})"
            : $"{f.From} -> fork({string.Join(", ", f.Targets)}, mode: {f.Mode})",
        ConnectionLine.Join j => $"join({string.Join(", ", j.Sources)}) -> {j.Target}",
        ConnectionLine.Router r => $"{r.From} -> router({string.Join(", ", r.Options)})",
        ConnectionLine.Loop l => l.MaxIterations is null
            ? $"{l.From} -> loop({l.LoopTarget}, exit: {l.ExitTarget})"
            : $"{l.From} -> loop({l.LoopTarget}, exit: {l.ExitTarget}, maxIterations: {l.MaxIterations})",
        ConnectionLine.SubFlow s => $"subflow({s.Name})",
        ConnectionLine.Interrupt i => $"interrupt({i.JobName})",
        ConnectionLine.Ask a => $"ask({a.JobName})",
        _ => throw new InvalidOperationException($"Unsupported connection line type: {connection.GetType().Name}")
    };

    private static Dictionary<string, ToolDirective> DiscoverTools(List<ConnectionLine> connections) =>
        connections
            .OfType<ConnectionLine.Tool>()
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new ToolDirective(g.Key, g.Last().Description, g.Last().Tags),
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, JobToolDirective> DiscoverJobToolDeclarations(List<ConnectionLine> connections) =>
        connections
            .OfType<ConnectionLine.Use>()
            .GroupBy(u => u.JobName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new JobToolDirective(g.Key, g.Last().ToolNames, g.Last().Semantic),
                StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Portable tool metadata declared in workflow DSL.
/// </summary>
public sealed record ToolDirective(string Name, string Description, IReadOnlyList<string> Tags);

/// <summary>
/// Per-job tool usage metadata declared in workflow DSL.
/// </summary>
public sealed record JobToolDirective(string JobName, IReadOnlyList<string> ToolNames, bool Semantic);

/// <summary>
/// Factory methods for parsing the workflow topology DSL into a <see cref="WorkflowScaffold{TState}"/>.
/// </summary>
public static class WorkflowScaffold
{
    /// <summary>
    /// Parses a multi-line DSL string into a scaffold.
    /// </summary>
    /// <param name="name">Workflow name.</param>
    /// <param name="dsl">
    /// Multi-line topology DSL. Each line is a connection or node directive:
    /// <c>a -&gt; b</c>, <c>a -&gt; fork(b, c)</c>, <c>join(a, b) -&gt; c</c>, <c>a -&gt; router(b, c)</c>,
    /// <c>a -&gt; loop(target, exit: x)</c>, <c>subflow(name)</c>, <c>interrupt(name)</c>,
    /// <c>ask(name)</c>.
    /// Lines starting with <c>#</c> are comments. Blank lines are ignored.
    /// </param>
    public static WorkflowScaffold<TState> Parse<TState>(string name, string dsl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(dsl);

        var connections = WorkflowDslParser.Parse(dsl);
        return new WorkflowScaffold<TState>(name, connections);
    }

    /// <summary>
    /// Parses individual DSL lines into a scaffold.
    /// </summary>
    public static WorkflowScaffold<TState> Parse<TState>(string name, IEnumerable<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(lines);

        var connections = WorkflowDslParser.Parse(lines);
        return new WorkflowScaffold<TState>(name, connections);
    }
}
