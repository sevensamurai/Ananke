using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Routing;

namespace Ananke.Orchestration;

/// <summary>
/// Validated, immutable description of a workflow: its jobs, connections, entry point, and metadata.
/// Built via <see cref="Workflow{TState}.Build"/>; pass to
/// <see cref="Execution.IWorkflowRunner.RunAsync{TState}"/> to execute.
/// </summary>
public sealed class WorkflowDefinition<TState>
{
    /// <summary>The name of the workflow.</summary>
    public string Name { get; }

    /// <summary>All registered jobs, keyed by name.</summary>
    public IReadOnlyDictionary<string, JobDescriptor<TState>> Jobs { get; }

    /// <summary>Ordered list of connections that form the execution graph.</summary>
    public IReadOnlyList<Connection> Connections { get; }

    /// <summary>Name of the first job to execute.</summary>
    public string EntryJob { get; }

    /// <summary>
    /// Workflow-level key/value metadata set via <see cref="Workflow{TState}.WithMetadata"/>.
    /// Flows into <see cref="WorkflowExecution{TState}.Metadata"/>, checkpoints, and trace attributes.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Fan-in descriptors that merge parallel branches back into a single execution path.</summary>
    public IReadOnlyList<JoinDescriptor<TState>> Joins { get; }

    /// <summary>
    /// Optional cost budget configuration. When set, the runner tracks token usage
    /// and terminates the workflow when the estimated cost exceeds the budget.
    /// </summary>
    public BudgetConfig? Budget { get; }

    internal WorkflowDefinition(
        string name,
        Dictionary<string, JobDescriptor<TState>> jobs,
        List<Connection> connections,
        string entryJob,
        IReadOnlyDictionary<string, string>? metadata = null,
        List<JoinDescriptor<TState>>? joins = null,
        BudgetConfig? budget = null)
    {
        Name = name;
        Jobs = new Dictionary<string, JobDescriptor<TState>>(jobs);
        Connections = [.. connections];
        EntryJob = entryJob;
        Metadata = metadata ?? new Dictionary<string, string>();
        Joins = joins is not null ? [.. joins] : [];
        Budget = budget;

        Validate();
    }

    internal string? ResolveDirectTarget(string fromJob)
    {
        foreach (var connection in Connections)
        {
            if (connection.From == fromJob && connection is DirectConnection direct)
                return direct.To;
        }
        return null;
    }

    internal IRouter<TState>? ResolveRouter(string fromJob)
    {
        foreach (var connection in Connections)
        {
            if (connection.From == fromJob && connection is RouterConnection<TState> router)
                return router.Router;
        }
        return null;
    }

    internal ForkConnection? ResolveFork(string fromJob)
    {
        foreach (var connection in Connections)
        {
            if (connection.From == fromJob && connection is ForkConnection fork)
                return fork;
        }
        return null;
    }

    internal LoopConnection<TState>? ResolveLoop(string fromJob)
    {
        foreach (var connection in Connections)
        {
            if (connection.From == fromJob && connection is LoopConnection<TState> loop)
                return loop;
        }
        return null;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidOperationException("Workflow name is required.");

        if (Jobs.Count == 0)
            throw new InvalidOperationException("Workflow must have at least one job.");

        if (!Jobs.ContainsKey(EntryJob))
            throw new InvalidOperationException($"Entry job '{EntryJob}' is not defined.");

        // Collect join sources — these legitimately lack outgoing connections
        var joinSources = new HashSet<string>();
        foreach (var join in Joins)
        {
            foreach (var source in join.Sources)
            {
                if (!Jobs.ContainsKey(source))
                    throw new InvalidOperationException($"Join source '{source}' is not defined as a job.");
                joinSources.Add(source);
            }

            if (join.Target != Workflow.EndMarker && !Jobs.ContainsKey(join.Target))
                throw new InvalidOperationException($"Join target '{join.Target}' is not defined as a job.");
        }

        var jobsWithConnections = new HashSet<string>();

        foreach (var connection in Connections)
        {
            if (!Jobs.ContainsKey(connection.From))
                throw new InvalidOperationException($"Connection from '{connection.From}' references an undefined job.");

            if (connection is DirectConnection direct)
            {
                if (direct.To != Workflow.EndMarker && !Jobs.ContainsKey(direct.To))
                    throw new InvalidOperationException($"Connection from '{connection.From}' to '{direct.To}' references an undefined job.");
            }
            else if (connection is ForkConnection fork)
            {
                foreach (var target in fork.Targets)
                {
                    if (!Jobs.ContainsKey(target))
                        throw new InvalidOperationException($"Fork target '{target}' from '{connection.From}' is not defined as a job.");
                }
            }
            else if (connection is LoopConnection<TState> loop)
            {
                if (!Jobs.ContainsKey(loop.LoopTarget))
                    throw new InvalidOperationException($"Loop target '{loop.LoopTarget}' from '{connection.From}' is not defined as a job.");
                if (loop.ExitTarget != Workflow.EndMarker && !Jobs.ContainsKey(loop.ExitTarget))
                    throw new InvalidOperationException($"Loop exit target '{loop.ExitTarget}' from '{connection.From}' is not defined as a job.");
                if (loop.MaxIterations < 1)
                    throw new InvalidOperationException($"Loop from '{connection.From}' must have MaxIterations >= 1.");
            }

            jobsWithConnections.Add(connection.From);
        }

        // Join sources are considered to have outgoing connections (through the join)
        foreach (var source in joinSources)
            jobsWithConnections.Add(source);

        var hasRouterConnections = Connections.Any(c => c is RouterConnection<TState>);

        if (!hasRouterConnections)
        {
            // BFS from entry job: direct connections + fork targets + join targets
            var reachable = new HashSet<string>();
            var queue = new Queue<string>([EntryJob]);
            while (queue.TryDequeue(out var current))
            {
                if (!reachable.Add(current)) continue;

                foreach (var conn in Connections.OfType<DirectConnection>()
                             .Where(c => c.From == current && c.To != Workflow.EndMarker))
                    queue.Enqueue(conn.To);

                foreach (var conn in Connections.OfType<ForkConnection>()
                             .Where(c => c.From == current))
                {
                    foreach (var target in conn.Targets)
                        queue.Enqueue(target);
                }

                foreach (var conn in Connections.OfType<LoopConnection<TState>>()
                             .Where(c => c.From == current))
                {
                    queue.Enqueue(conn.LoopTarget);
                    if (conn.ExitTarget != Workflow.EndMarker)
                        queue.Enqueue(conn.ExitTarget);
                }

                foreach (var join in Joins.Where(j => j.Sources.Contains(current)))
                {
                    if (join.Target != Workflow.EndMarker)
                        queue.Enqueue(join.Target);
                }
            }

            var unreachableJobs = Jobs.Keys.Where(j => !reachable.Contains(j)).ToList();
            if (unreachableJobs.Count > 0)
                throw new InvalidOperationException(
                    $"Unreachable job(s): {string.Join(", ", unreachableJobs)}. " +
                    $"Every job except the entry job must be reachable from '{EntryJob}'.");
        }

        foreach (var job in Jobs.Keys.Where(j => !jobsWithConnections.Contains(j)))
        {
            if (Jobs.Count > 1)
                throw new InvalidOperationException($"Job '{job}' has no outgoing connection. Use .Then(\"{job}\", End) to mark it as terminal.");
        }

        if (Jobs.TryGetValue(EntryJob, out var entryDescriptor) && entryDescriptor.Interrupt == Ananke.Orchestration.Jobs.InterruptMode.Before)
            throw new InvalidOperationException(
                $"InterruptBefore cannot be applied to the entry job '{EntryJob}'. " +
                "No work has been performed yet — handle pre-workflow approval outside the workflow.");

        // Join sources must not have their own outgoing connections — the join provides continuation
        foreach (var source in joinSources)
        {
            if (Connections.Any(c => c.From == source))
                throw new InvalidOperationException(
                    $"Join source '{source}' must not have outgoing connections. " +
                    "The Join provides the continuation to the target job.");
        }
    }
}
