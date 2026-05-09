using Ananke.Design;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Healing;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Ananke.Organics.Kernel;

/// <summary>
/// Growth-aware orchestrator that monitors workflow complexity, evaluates
/// division policies, and flows proposals through an
/// <see cref="IDivisionApprovalGate"/>. Does NOT manage cell lifecycle
/// directly — delegates to an <see cref="IWorkflowHost"/> for spawn/kill.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separation of concerns:</b>
/// <see cref="IWorkflowHost"/> = cell lifecycle (infra-specific: in-process, Docker, K8s).
/// <see cref="OrganicHost"/> = growth brain (monitor → policy → gate → signal).
/// They compose; they don't inherit.
/// </para>
/// <para>
/// Workflows join via <c>OrganicWorkflowExtensions.JoinHost</c>,
/// which returns an <c>OrganicWorkflow&lt;TState&gt;</c> wrapper. Every
/// execution through the wrapper is observed — the inner workflow's runner,
/// checkpointing, and tracing are never touched.
/// </para>
/// </remarks>
public sealed class OrganicHost : IAsyncDisposable
{
    private readonly IWorkflowHost _cellHost;
    private readonly ICapabilityMap _landscape;
    private readonly OrganicGrowthOptions _options;
    private readonly ConcurrentDictionary<string, int> _executionCounts = new();
    private readonly ConcurrentBag<Task> _inflightDivisions = [];
    private readonly Channel<ObservationEntry> _queue = Channel.CreateUnbounded<ObservationEntry>();
    private readonly CancellationTokenSource _loopCts = new();
    private readonly Task _loopTask;
    private readonly Task? _pollingTask;

    // Signals the next background processing pass per workflow name.
    // Channel<bool> is used (not TCS) so signals written before the test
    // calls WhenProcessedAsync are never lost — the test just reads them.
    private readonly ConcurrentDictionary<string, Channel<bool>> _processedSignals = new();

    // Signals each EvaluateAndSignalAsync completion per workflow name.
    // Covers both the channel-driven and remote-polling paths.
    private readonly ConcurrentDictionary<string, Channel<bool>> _evaluatedSignals = new();

    /// <summary>
    /// Creates a new organic host with the given cell host, capability landscape,
    /// and growth options.
    /// </summary>
    /// <param name="cellHost">
    /// The underlying cell host used for spawn/kill operations during division.
    /// Infra-specific: <see cref="InProcessWorkflowHost"/> for dev/demo,
    /// Docker/K8s adapters for production.
    /// </param>
    /// <param name="landscape">Capability landscape for sensing.</param>
    /// <param name="options">Growth configuration: policy, gate, monitor, interval.</param>
    public OrganicHost(
        IWorkflowHost cellHost,
        ICapabilityMap landscape,
        OrganicGrowthOptions options)
    {
        ArgumentNullException.ThrowIfNull(cellHost);
        ArgumentNullException.ThrowIfNull(landscape);
        ArgumentNullException.ThrowIfNull(options);

        _cellHost = cellHost;
        _landscape = landscape;
        _options = options;

        options.Validate();

        _loopTask = Task.Run(() => BackgroundLoopAsync(_loopCts.Token));

        if (_options.RemoteCellSource is not null)
            _pollingTask = Task.Run(() => RemotePollingLoopAsync(_loopCts.Token));
    }

    // ── Cell lifecycle delegation ────────────────────────────────────

    /// <summary>
    /// The underlying cell host used for spawn/kill operations during
    /// division execution. Exposed for advanced scenarios (e.g., manual
    /// cell management alongside organic growth).
    /// </summary>
    public IWorkflowHost CellHost => _cellHost;

    /// <summary>
    /// The health monitor used by this host. Exposed for diagnostics
    /// and snapshot inspection.
    /// </summary>
    public IHealthMonitor GetMonitor() => _options.Monitor;

    // ── Observability events (logging, dashboards — NOT governance) ──

    /// <summary>
    /// Raised when a division policy proposes a split. For logging and
    /// metrics only — the <see cref="IDivisionApprovalGate"/> controls
    /// whether the division proceeds.
    /// </summary>
    public event Func<DivisionSignal, Task>? OnDivisionProposed;

    /// <summary>Raised after the approval gate approves a division.</summary>
    public event Func<DivisionSignal, Task>? OnDivisionApproved;

    /// <summary>Raised after the approval gate rejects a division.</summary>
    public event Func<DivisionSignal, Task>? OnDivisionRejected;

    /// <summary>
    /// Raised after a division executes successfully. Only fires when
    /// <see cref="OrganicGrowthOptions.Divider"/> is configured.
    /// </summary>
    public event Func<DivisionSignal, Task>? OnDivisionCompleted;

    /// <summary>
    /// Raised when division execution fails (parent resumes). Only fires
    /// when <see cref="OrganicGrowthOptions.Divider"/> is configured.
    /// </summary>
    public event Func<DivisionSignal, Task>? OnDivisionFailed;

    // ── Called by OrganicWorkflow<T> — non-blocking ──────────────────

    /// <summary>
    /// Queues a completed execution for background evaluation.
    /// Called by <c>OrganicWorkflow&lt;TState&gt;</c> — non-blocking.
    /// Records both successful and failed executions for health monitoring.
    /// </summary>
    internal void ObserveExecution<TState>(string workflowName, WorkflowExecution<TState> execution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentNullException.ThrowIfNull(execution);

        _queue.Writer.TryWrite(new ObservationEntry(workflowName, ex =>
        {
            _options.Monitor.Record(execution);
        }));
    }

    /// <summary>
    /// Queues a stream completion event for background evaluation.
    /// Called by <c>OrganicWorkflow&lt;TState&gt;</c> during <c>StreamAsync</c>.
    /// </summary>
    internal void ObserveCompleted<TState>(string workflowName, WorkflowCompleted<TState> completed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentNullException.ThrowIfNull(completed);

        if (!completed.Result.Success)
            return;

        // WorkflowCompleted carries the Result but not the full Execution.
        // Record a counter bump for evaluation interval purposes only.
        _queue.Writer.TryWrite(new ObservationEntry(workflowName, _ => { }));
    }

    // ── Called by JoinHost extension ──────────────────────────────────

    /// <summary>
    /// Registers a workflow's structural profile for monitoring.
    /// Called by <c>OrganicWorkflowExtensions.JoinHost</c>.
    /// </summary>
    internal void Register(string workflowName, ToolKit? toolKit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        var profile = toolKit is not null
            ? StructuralProfileFactory.FromToolKit(toolKit, jobCount: 1)
            : new StructuralProfile
            {
                ToolCount = 0,
                JobCount = 1,
                TagClusterCount = 1,
                ResourceSpan = 1,
                ContextUtilization = 0f
            };

        if (_options.Monitor is WorkflowExecutionMonitor monitor)
            monitor.RegisterWorkflow(workflowName, profile);
    }

    // ── Internal observation hooks ──────────────────────────────────────────
    // Exposed only to Ananke.Organics.Tests via InternalsVisibleTo.

    /// <summary>
    /// Returns a <see cref="Task"/> that completes the next time the background
    /// loop finishes processing an observation for <paramref name="workflowName"/>.
    /// Always pair with <c>WaitAsync(TimeSpan.FromSeconds(5))</c>.
    /// </summary>
    internal Task WhenProcessedAsync(string workflowName)
    {
        var ch = _processedSignals.GetOrAdd(workflowName,
            _ => Channel.CreateUnbounded<bool>());
        return ch.Reader.ReadAsync().AsTask();
    }

    /// <summary>
    /// Returns a <see cref="Task"/> that completes the next time
    /// <see cref="EvaluateAndSignalAsync"/> finishes for <paramref name="workflowName"/>.
    /// Covers both the channel-driven path and the remote-polling path.
    /// Always pair with <c>WaitAsync(TimeSpan.FromSeconds(5))</c>.
    /// </summary>
    internal Task WhenEvaluatedAsync(string workflowName)
    {
        var ch = _evaluatedSignals.GetOrAdd(workflowName,
            _ => Channel.CreateUnbounded<bool>());
        return ch.Reader.ReadAsync().AsTask();
    }

    // ── Background evaluation loop ───────────────────────────────────

    private async Task RemotePollingLoopAsync(CancellationToken ct)
    {
        var source = _options.RemoteCellSource!;
        var interval = _options.RemotePollingInterval;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct);

                try
                {
                    var remoteNames = await source.GetRemoteCellNamesAsync(ct);

                    foreach (var name in remoteNames)
                    {
                        try
                        {
                            await EvaluateAndSignalAsync(name, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            return;
                        }
                        catch
                        {
                            // Swallow per-cell failures to keep polling alive.
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Swallow source failures to keep polling alive.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected — host is being disposed.
        }
    }

    private async Task BackgroundLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    // Record in monitor
                    entry.RecordAction(entry);

                    // Increment and check evaluation interval
                    var count = _executionCounts.AddOrUpdate(
                        entry.WorkflowName, 1, (_, c) => c + 1);

                    if (count % _options.EvaluationInterval == 0)
                        await EvaluateAndSignalAsync(entry.WorkflowName, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Swallow per-entry failures to keep the loop alive.
                    // Future: structured logging.
                }
                finally
                {
                    // Signal any test waiting for this workflow to be processed.
                    // Using a channel so signals written before WhenProcessedAsync
                    // is called are never lost.
                    var ch = _processedSignals.GetOrAdd(entry.WorkflowName,
                        _ => Channel.CreateUnbounded<bool>());
                    ch.Writer.TryWrite(true);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected — host is being disposed.
        }
    }

    private async Task EvaluateAndSignalAsync(string workflowName, CancellationToken ct)
    {
        try
        {
            var snapshot = await _options.Monitor.GetSnapshotAsync(workflowName, ct);

            // L3: classify metabolism and stamp snapshot
            var metabolism = _options.MetabolicThresholds.Classify(snapshot);
            snapshot = snapshot with { Metabolism = metabolism };

            // L5: report to mesh aggregator
            _options.MeshAggregator.Report(workflowName, metabolism);

            var manifest = _options.ManifestFactory?.Invoke(workflowName)
                ?? CreateMinimalManifest(workflowName);

            var plan = await _options.Policy.EvaluateAsync(snapshot, manifest, ct);
            if (plan is null)
                return;

            // Emit OnDivisionProposed (observability)
            var proposedSignal = new DivisionSignal
            {
                WorkflowName = workflowName,
                Snapshot = snapshot,
                Plan = plan,
                Timestamp = DateTimeOffset.UtcNow
            };

            if (OnDivisionProposed is not null)
                await OnDivisionProposed(proposedSignal);

            // Governance checkpoint: always go through the gate
            var approval = await _options.ApprovalGate.ReviewAsync(plan, snapshot, ct);

            var resultSignal = proposedSignal with { Approval = approval };

            if (approval.IsApproved)
            {
                if (OnDivisionApproved is not null)
                    await OnDivisionApproved(resultSignal);

                // Record baseline for outcome tracking
                var divisionId = Guid.NewGuid().ToString("N");
                _options.OutcomeTracker?.RecordBaseline(divisionId, snapshot);

                // Execute division if divider is configured
                if (_options.Divider is not null)
                {
                    var finalPlan = approval.RevisedPlan ?? plan;

                    // Fire on background task — division may take 30+ seconds
                    // for federation deploys. The observation loop continues.
                    var divisionTask = Task.Run(async () =>
                    {
                        try
                        {
                            // Resolve the transition: use configured one or default.
                            var transition = _options.Transition
                                ?? new StopTheWorldDivisionTransition(_cellHost, TimeSpan.FromSeconds(30));

                            // Phase 1: drain — stop parent accepting new work.
                            await transition.BeginDrainAsync(
                                workflowName, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

                            DivisionResult result;
                            try
                            {
                                result = await _options.Divider.DivideAsync(
                                    finalPlan, manifest, _options.SharedMemory!, ct);
                            }
                            catch
                            {
                                // Division failed — resume parent so it keeps serving requests.
                                await _cellHost.ResumeAsync(workflowName).ConfigureAwait(false);
                                throw;
                            }

                            // Phase 2: switchover — children are now alive, update routing.
                            var childIds = result.NewManifests.Select(m => m.Name).ToList();
                            await transition.SwitchoverAsync(finalPlan, childIds, ct).ConfigureAwait(false);

                            // Phase 3: confirm — release transition resources.
                            await transition.CompleteAsync(workflowName, ct).ConfigureAwait(false);

                            // Remove the (now dead) parent from the capability landscape.
                            _landscape.Remove(workflowName);

                            // L1: record parent death in lineage store.
                            await _options.Lineage.RecordDeathAsync(
                                workflowName, DateTimeOffset.UtcNow, "division", ct);

                            // L5: remove parent from mesh aggregator.
                            _options.MeshAggregator.Forget(workflowName);

                            // L1: record child births.
                            var parentRecord = await _options.Lineage.GetAsync(workflowName, ct);
                            var parentGeneration = parentRecord?.Generation ?? 0;

                            // Register each new child cell's capabilities.
                            foreach (var newManifest in result.NewManifests)
                            {
                                var matchingChild = finalPlan.Children
                                    .FirstOrDefault(c => c.Name == newManifest.Name);

                                _landscape.Register(new WorkflowSignal
                                {
                                    WorkflowName = newManifest.Name,
                                    Domain = matchingChild?.Domain ?? newManifest.Name,
                                    Capabilities = newManifest.Jobs.Keys.ToList(),
                                    Timestamp = DateTimeOffset.UtcNow,
                                    SplitFrom = workflowName
                                });

                                // Only write lineage if the divider hasn't already written it.
                                if (await _options.Lineage.GetAsync(newManifest.Name, ct) is null)
                                {
                                    await _options.Lineage.RecordBirthAsync(new CellLineage
                                    {
                                        CellId = newManifest.Name,
                                        WorkflowName = newManifest.Name,
                                        ParentCellId = workflowName,
                                        Generation = parentGeneration + 1,
                                        BornAt = DateTimeOffset.UtcNow,
                                        DivisionReason = finalPlan.Reason,
                                        InheritedDomains = matchingChild is not null
                                            ? [matchingChild.Domain]
                                            : []
                                    }, ct);
                                }
                            }

                            // Update the domain router so it learns the new topology.
                            if (_options.DomainRouter is not null)
                            {
                                var toolDescriptions = finalPlan.Children
                                    .SelectMany(c => c.Tools)
                                    .Distinct()
                                    .ToDictionary(t => t, t => t);

                                await _options.DomainRouter.IndexAsync(
                                    finalPlan.Children, toolDescriptions, ct);
                            }

                            // Item 1 (C-1): close the learning loop.
                            // Wait for child cells to stabilize, then sample their
                            // snapshots and call RewardAsync to feed empirical memory.
                            if (_options.OutcomeTracker is not null
                                && result.NewManifests.Count > 0)
                            {
                                if (_options.StabilizationWindowMs > 0)
                                    await Task.Delay(_options.StabilizationWindowMs, ct);

                                // Child cells may not have executed yet, so GetSnapshotAsync
                                // may throw for unregistered names — skip those silently.
                                var childSnapshots = new List<ComplexitySnapshot>();
                                foreach (var m in result.NewManifests)
                                {
                                    try { childSnapshots.Add(await _options.Monitor.GetSnapshotAsync(m.Name, ct)); }
                                    catch (InvalidOperationException) { }
                                }

                                await _options.OutcomeTracker.RewardAsync(
                                    divisionId, childSnapshots, finalPlan, ct);
                            }

                            if (OnDivisionCompleted is not null)
                                await OnDivisionCompleted(resultSignal);
                        }
                        catch (OperationCanceledException)
                        {
                            // Host is shutting down — leave events unfired.
                        }
                        catch (Exception)
                        {
                            if (OnDivisionFailed is not null)
                                await OnDivisionFailed(resultSignal);
                        }
                    }, CancellationToken.None);

                    _inflightDivisions.Add(divisionTask);
                }
            }
            else
            {
                if (OnDivisionRejected is not null)
                    await OnDivisionRejected(resultSignal);
            }
        } // end try
        finally
        {
            // Signal any test waiting for EvaluateAndSignalAsync to complete.
            // Fires for both the channel-driven path and the remote-polling path.
            var ch = _evaluatedSignals.GetOrAdd(workflowName,
                _ => Channel.CreateUnbounded<bool>());
            ch.Writer.TryWrite(true);
        }
    }

    private static WorkflowManifest CreateMinimalManifest(string workflowName) => new()
    {
        Name = workflowName,
        Models = [],
        Jobs = new Dictionary<string, JobDefinition>
        {
            ["default"] = new() { Type = "agent" }
        },
        Connections = []
    };

    // ── IAsyncDisposable ─────────────────────────────────────────────

    /// <summary>
    /// Stops the background evaluation loop and disposes the cell host.
    /// Waits up to <see cref="OrganicGrowthOptions.DivisionShutdownTimeoutMs"/>
    /// for any in-flight division tasks to complete before proceeding.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _loopCts.CancelAsync();

        _queue.Writer.TryComplete();

        try
        {
            await _loopTask;
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        // Wait for in-flight division background tasks to finish.
        var allDivisions = _inflightDivisions.ToArray();
        if (allDivisions.Length > 0)
        {
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(_options.DivisionShutdownTimeoutMs));
            try
            {
                await Task.WhenAll(allDivisions).WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout elapsed — abandon remaining tasks and proceed.
            }
            catch
            {
                // Individual task faults are already handled inside each task.
            }
        }

        _loopCts.Dispose();

        await _cellHost.DisposeAsync();
    }

    // ── Internal types ───────────────────────────────────────────────

    private sealed record ObservationEntry(
        string WorkflowName,
        Action<ObservationEntry> RecordAction);
}
