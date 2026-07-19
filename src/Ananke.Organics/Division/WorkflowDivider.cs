using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Sensing;

namespace Ananke.Organics.Division;

/// <summary>
/// Default <see cref="IWorkflowDivider"/> that orchestrates cell division by
/// composing existing infrastructure: snapshot derivation, skill seeding,
/// workflow activation, and cell lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// The divider is stateless — all state lives in the components it composes.
/// Each <see cref="DivideAsync"/> call follows a derive → seed → activate →
/// spawn → confirm → kill sequence. If any child fails to start, all spawned
/// children are torn down and the parent survives — no partial division.
/// </para>
/// <para>
/// For federated divisions (where <see cref="ChildSpec.TargetPlatform"/> is set),
/// the divider is transparent — it calls <see cref="IWorkflowHost.StartAsync"/> and
/// the underlying host (e.g. <c>FederatedWorkflowHost</c>) routes each child to
/// the appropriate platform automatically.
/// </para>
/// <para>
/// <b>Design note — routing after division:</b> The current implementation spawns
/// children and kills the parent, but does <b>not</b> handle request routing to
/// the new cells. After division, incoming requests that targeted the parent need
/// to be routed to the appropriate child based on domain. This routing concern
/// (load balancing, domain-based dispatch, graceful switchover) is outside the
/// divider's scope and must be handled by the caller (e.g. <see cref="IRequestRouter"/>
/// or <c>OrganicHost</c>). The capability landscape updates automatically as
/// children emit heartbeats and the parent is removed, but consumers that cached
/// the parent's name will need to re-resolve. The routing/load-balancing lifecycle
/// around division transitions is a known limitation to address in a future revision.
/// </para>
/// </remarks>
public sealed class WorkflowDivider(
    IWorkflowHost host,
    ICapabilityMap landscape,
    IWorkflowActivatorFactory activatorFactory,
    DivisionOptions? options = null,
    ILineageStore? lineageStore = null) : IWorkflowDivider
{
    private readonly DivisionOptions _options = options ?? new DivisionOptions();
    private readonly ILineageStore _lineage = lineageStore ?? new InMemoryLineageStore();

    /// <inheritdoc />
    public async Task<DivisionResult> DivideAsync(
        DivisionPlan plan,
        WorkflowManifest parentManifest,
        IEmpiricalMemory parentMemory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(parentManifest);
        ArgumentNullException.ThrowIfNull(parentMemory);

        // ── Step 1: Derive child snapshots ──────────────────────────
        var childSnapshots = DeriveSnapshots(plan, parentManifest);

        // ── Step 2: Derive memory profiles ──────────────────────────
        var memoryProfiles = DeriveMemoryProfiles(plan);

        // ── Step 3: Derive child manifests ──────────────────────────
        var childManifests = DeriveManifests(plan, parentManifest);

        // ── Step 4: Build routing table ─────────────────────────────
        var routingTable = plan.Children
            .ToDictionary(c => c.Domain, c => c.Name);

        // ── Build result early (simulate mode returns here) ─────────
        var result = new DivisionResult
        {
            NewManifests = childManifests,
            RoutingTable = routingTable,
            MemoryProfiles = memoryProfiles
        };

        if (_options.Simulate)
            return result;

        var transition = _options.Transition;

        // 5.9: Begin drain BEFORE spawning so in-flight requests on the parent complete
        // (or time out) before new children take over. Only invoked when a transition
        // orchestrator is configured.
        if (transition is not null)
            await transition.BeginDrainAsync(
                plan.ParentWorkflow,
                _options.HealthConfirmationTimeout,
                ct).ConfigureAwait(false);

        // ── Step 5: Activate children into runnable loops ───────────
        var childLoops = new List<(string Name, WorkflowSnapshot Snapshot, Func<CancellationToken, Task> Loop)>();
        for (var i = 0; i < childSnapshots.Count; i++)
        {
            var snapshot = childSnapshots[i];
            var profile = memoryProfiles[i];
            var loop = activatorFactory.CreateLoop(snapshot, profile);
            childLoops.Add((snapshot.Name, snapshot, loop));
        }

        // ── Step 6: Spawn children ──────────────────────────────────
        var spawnedNames = new List<string>();
        try
        {
            foreach (var (name, snapshot, loop) in childLoops)
            {
                await host.StartAsync(name, loop, ct);
                spawnedNames.Add(name);
            }

            // ── Step 7: Confirm children started ────────────────────
            await ConfirmChildrenStarted(spawnedNames, ct);

            // ── Step 7b: Record child births in lineage store ────
            var parentRecord = await _lineage.GetAsync(plan.ParentWorkflow, ct);
            var parentGeneration = parentRecord?.Generation ?? 0;
            foreach (var child in plan.Children)
            {
                await _lineage.RecordBirthAsync(new CellLineage
                {
                    CellId = child.Name,
                    WorkflowName = child.Name,
                    ParentCellId = plan.ParentWorkflow,
                    Generation = parentGeneration + 1,
                    BornAt = DateTimeOffset.UtcNow,
                    DivisionReason = plan.Reason,
                    InheritedDomains = [child.Domain]
                }, ct);
            }

            // 5.9: Switchover AFTER children are confirmed so new requests go to children
            // only once they are actually healthy and ready to accept work.
            if (transition is not null)
                await transition.SwitchoverAsync(plan, spawnedNames, ct).ConfigureAwait(false);
        }
        catch
        {
            // Abort: tear down any spawned children, parent survives
            foreach (var name in spawnedNames)
            {
                try { await host.StopAsync(name); } catch { /* best-effort cleanup */ }
                landscape.Remove(name);
            }

            throw;
        }

        // ── Step 8: Kill parent ─────────────────────────────────────
        await host.StopAsync(plan.ParentWorkflow);
        landscape.Remove(plan.ParentWorkflow);

        // 5.9: Complete AFTER parent is fully removed so the transition orchestrator
        // can release drain queues only once the old cell is gone from routing.
        if (transition is not null)
            await transition.CompleteAsync(plan.ParentWorkflow, ct).ConfigureAwait(false);

        return result;
    }

    // ── Snapshot derivation ─────────────────────────────────────────

    private static IReadOnlyList<WorkflowSnapshot> DeriveSnapshots(
        DivisionPlan plan,
        WorkflowManifest parentManifest)
    {
        var snapshots = new List<WorkflowSnapshot>();

        foreach (var child in plan.Children)
        {
            var childJobs = new Dictionary<string, JobSnapshot>();
            foreach (var jobName in child.Jobs)
            {
                if (!parentManifest.Jobs.TryGetValue(jobName, out var jobDef))
                    continue;

                childJobs[jobName] = new JobSnapshot
                {
                    Type = jobDef.Type,
                    ModelAlias = jobDef.ModelAlias,
                    SystemPrompt = child.SystemPromptOverride ?? jobDef.SystemPrompt,
                    MaxToolRounds = jobDef.MaxToolRounds
                };
            }

            // Filter connections to only those referencing this child's jobs
            var childJobSet = child.Jobs.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var childConnections = FilterConnections(parentManifest.Connections, childJobSet);

            // Carry over model aliases referenced by the child's jobs
            var childModels = new Dictionary<string, ModelSnapshot>();
            foreach (var (_, jobSnap) in childJobs)
            {
                var alias = jobSnap.ModelAlias ?? "default";
                if (!childModels.ContainsKey(alias) &&
                    parentManifest.Models.TryGetValue(alias, out var modelDef))
                {
                    childModels[alias] = new ModelSnapshot
                    {
                        Provider = modelDef.Provider,
                        Model = modelDef.Model,
                        Endpoint = modelDef.Endpoint
                    };
                }
            }

            snapshots.Add(new WorkflowSnapshot
            {
                Name = child.Name,
                Domain = child.Domain,
                SplitFrom = plan.ParentWorkflow,
                Tools = child.Tools.ToList(),
                Connections = childConnections,
                Jobs = childJobs,
                Models = childModels,
                MemoryProfile = new MemoryProfile
                {
                    Domains = [child.Domain, "general"],
                    LineageTags = [plan.ParentWorkflow]
                }
            });
        }

        return snapshots;
    }

    private static List<string> FilterConnections(
        List<string> parentConnections,
        HashSet<string> childJobs)
    {
        // Keep connections where both source and target are in the child's job set
        // or are special tokens (End, Start)
        var result = new List<string>();
        foreach (var conn in parentConnections)
        {
            var parts = conn.Split("->", StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;

            var source = parts[0];
            var target = parts[1];

            var sourceOk = childJobs.Contains(source) ||
                           source.Equals("Start", StringComparison.OrdinalIgnoreCase);
            var targetOk = childJobs.Contains(target) ||
                           target.Equals("End", StringComparison.OrdinalIgnoreCase);

            if (sourceOk && targetOk)
                result.Add(conn);
        }

        // If no connections survived, create a simple chain: job1 -> job2 -> ... -> End
        if (result.Count == 0 && childJobs.Count > 0)
        {
            var jobs = childJobs.ToList();
            for (var i = 0; i < jobs.Count - 1; i++)
                result.Add($"{jobs[i]} -> {jobs[i + 1]}");
            result.Add($"{jobs[^1]} -> End");
        }

        return result;
    }

    // ── Memory profile derivation ───────────────────────────────────

    private static IReadOnlyList<MemoryProfile> DeriveMemoryProfiles(DivisionPlan plan) =>
        plan.Children.Select(child => new MemoryProfile
        {
            Domains = [child.Domain, "general"],
            LineageTags = [plan.ParentWorkflow]
        }).ToList();

    // ── Manifest derivation ─────────────────────────────────────────

    private static IReadOnlyList<WorkflowManifest> DeriveManifests(
        DivisionPlan plan,
        WorkflowManifest parentManifest)
    {
        var manifests = new List<WorkflowManifest>();

        foreach (var child in plan.Children)
        {
            var childJobs = new Dictionary<string, JobDefinition>();
            foreach (var jobName in child.Jobs)
            {
                if (parentManifest.Jobs.TryGetValue(jobName, out var parentJob))
                {
                    childJobs[jobName] = new JobDefinition
                    {
                        Type = parentJob.Type,
                        ModelAlias = parentJob.ModelAlias,
                        SystemPrompt = child.SystemPromptOverride ?? parentJob.SystemPrompt,
                        MaxToolRounds = parentJob.MaxToolRounds
                    };
                }
            }

            // Carry over referenced models
            var childModels = new Dictionary<string, ModelDefinition>();
            foreach (var (_, jobDef) in childJobs)
            {
                var alias = jobDef.ModelAlias ?? "default";
                if (!childModels.ContainsKey(alias) &&
                    parentManifest.Models.TryGetValue(alias, out var modelDef))
                {
                    childModels[alias] = new ModelDefinition
                    {
                        Provider = modelDef.Provider,
                        Model = modelDef.Model,
                        Endpoint = modelDef.Endpoint
                    };
                }
            }

            var childJobSet = child.Jobs.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var childConnections = FilterConnections(parentManifest.Connections, childJobSet);

            manifests.Add(new WorkflowManifest
            {
                Name = child.Name,
                Models = childModels,
                Jobs = childJobs,
                Connections = childConnections
            });
        }

        return manifests;
    }

    // ── Startup confirmation ───────────────────────────────────────

    private async Task ConfirmChildrenStarted(
        IReadOnlyList<string> childNames,
        CancellationToken ct)
    {
        var timeout = _options.HealthConfirmationTimeout;
        var deadline = DateTimeOffset.UtcNow + timeout;

        var pending = new HashSet<string>(childNames);

        while (pending.Count > 0)
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException(
                    $"Division health confirmation timed out after {timeout.TotalSeconds}s. " +
                    $"Children not yet active: [{string.Join(", ", pending)}]");

            ct.ThrowIfCancellationRequested();

            var active = host.ListActive();
            foreach (var name in active)
                pending.Remove(name);

            if (pending.Count > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
        }
    }
}
