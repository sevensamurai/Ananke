using Ananke.Design.Dsl;
using Ananke.Orchestration;
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
    private readonly HashSet<string> _jobNames;
    private readonly Dictionary<string, IJob<TState>> _boundJobs = [];
    private readonly Dictionary<string, Func<TState, CancellationToken, Task<TState>>> _boundDelegates = [];
    private readonly Dictionary<string, Func<TState[], TState>> _mergeFunctions = [];
    private readonly Dictionary<string, IRouter<TState>> _routers = [];

    internal WorkflowScaffold(string name, List<ConnectionLine> connections)
    {
        _name = name;
        _connections = connections;
        _jobNames = DiscoverJobNames(connections);

        if (_jobNames.Count == 0)
            throw new InvalidOperationException("DSL produced no job names. At least one connection is required.");
    }

    /// <summary>
    /// All job names discovered from the parsed topology.
    /// </summary>
    public IReadOnlySet<string> JobNames => _jobNames;

    /// <summary>
    /// Job names that have not yet been bound to an implementation.
    /// </summary>
    public IReadOnlySet<string> UnboundJobs =>
        new HashSet<string>(_jobNames.Where(n => !_boundJobs.ContainsKey(n) && !_boundDelegates.ContainsKey(n)));

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
    /// Builds the fully bound workflow. Throws if any jobs, merges, or routers are unbound.
    /// </summary>
    public Workflow<TState> Build()
    {
        ValidateAllBound();

        var workflow = new Workflow<TState>(_name);

        // Register jobs in discovery order (first seen = entry job)
        foreach (var name in DiscoverOrderedJobNames(_connections))
        {
            if (_boundJobs.TryGetValue(name, out var job))
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
            }
        }

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
            }
        }

        return ordered;
    }

    private static HashSet<string> DiscoverJobNames(List<ConnectionLine> connections) =>
        [.. DiscoverOrderedJobNames(connections)];
}

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
    /// Multi-line topology DSL. Each line is one connection:
    /// <c>a -&gt; b</c>, <c>a -&gt; fork(b, c)</c>, <c>join(a, b) -&gt; c</c>, <c>a -&gt; router(b, c)</c>.
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
