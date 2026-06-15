using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Budget;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Ananke.Abstractions.Tracing;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Middleware;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Execution;

public sealed partial class WorkflowRunner : IWorkflowRunner
{
    private readonly ICheckpointStore? _checkpointStore;
    private readonly IReadOnlyList<IWorkflowJobMiddleware<object>> _middlewares;
    private readonly IWorkflowTracer _tracer;
    private readonly bool _storeCompletions;
    private readonly ILogger<WorkflowRunner> _logger;
    private readonly TimeSpan _checkpointTtl;

    public WorkflowRunner(
        ICheckpointStore? checkpointStore = null,
        IEnumerable<IWorkflowJobMiddleware<object>>? middlewares = null,
        IWorkflowTracer? tracer = null,
        bool storeCompletions = true,
        ILoggerFactory? loggerFactory = null,
        TimeSpan? checkpointTtl = null)
    {
        _checkpointStore = checkpointStore;
        _middlewares = middlewares?.ToList() ?? [];
        _tracer = tracer ?? NullTracer.Instance;
        _storeCompletions = storeCompletions;
        _logger = loggerFactory?.CreateLogger<WorkflowRunner>()
            ?? NullLogger<WorkflowRunner>.Instance;
        _checkpointTtl = checkpointTtl ?? TimeSpan.FromDays(7);
    }

    public async Task<WorkflowExecution<TState>> RunAsync<TState>(
        WorkflowDefinition<TState> definition,
        TState initialState,
        CancellationToken ct = default)
    {
        var execution = new WorkflowExecution<TState>(definition.Name, initialState, definition.Metadata);
        return await ExecuteAsync(definition, execution, definition.EntryJob, ct);
    }

    public async Task<WorkflowExecution<TState>> ResumeAsync<TState>(
        WorkflowDefinition<TState> definition,
        Checkpoint<TState> checkpoint,
        CancellationToken ct = default)
    {
        var execution = WorkflowExecution<TState>.FromCheckpoint(checkpoint);

        if (checkpoint.InterruptedBeforeJob is not null)
            return await ExecuteAsync(definition, execution, checkpoint.InterruptedBeforeJob, ct, skipFirstInterrupt: true);

        var nextJob = await ResolveResumeTargetAsync(definition, checkpoint.CurrentJob, execution, ct);
        var skipBody = definition.ResolveFork(checkpoint.CurrentJob) is not null;
        return await ExecuteAsync(definition, execution, nextJob, ct, skipFirstJobExecution: skipBody);
    }

    public async Task<WorkflowExecution<TState>> ResumeAsync<TState>(
        WorkflowDefinition<TState> definition,
        Checkpoint<TState> checkpoint,
        Func<TState, TState> stateTransform,
        CancellationToken ct = default)
    {
        var execution = WorkflowExecution<TState>.FromCheckpoint(checkpoint);
        execution.State = stateTransform(execution.State);

        if (checkpoint.InterruptedBeforeJob is not null)
            return await ExecuteAsync(definition, execution, checkpoint.InterruptedBeforeJob, ct, skipFirstInterrupt: true);

        var nextJob = await ResolveResumeTargetAsync(definition, checkpoint.CurrentJob, execution, ct);
        var skipBody = definition.ResolveFork(checkpoint.CurrentJob) is not null;
        return await ExecuteAsync(definition, execution, nextJob, ct, skipFirstJobExecution: skipBody);
    }

    /// <summary>
    /// Resolves the next job to execute when resuming from a checkpoint. Handles
    /// loop connections that <see cref="ResolveNextJobAsync"/> cannot because loop
    /// evaluation requires access to the execution's loop counters.
    /// </summary>
    private static async Task<string?> ResolveResumeTargetAsync<TState>(
        WorkflowDefinition<TState> definition,
        string currentJob,
        WorkflowExecution<TState> execution,
        CancellationToken ct)
    {
        var loopConn = definition.ResolveLoop(currentJob);
        if (loopConn is not null)
        {
            var iteration = execution.IncrementLoopCounter(currentJob);

            if (loopConn.Until(execution.State) || iteration >= loopConn.MaxIterations)
            {
                execution.ResetLoopCounter(currentJob);
                return loopConn.ExitTarget;
            }
            return loopConn.LoopTarget;
        }

        // 4.7: Honour fork connections on the resume path the same way ExecuteAsync does.
        // A checkpoint saved after a forked job should resume into the fork rather than
        // silently skipping it via ResolveNextJobAsync.
        var forkConn = definition.ResolveFork(currentJob);
        if (forkConn is not null)
        {
            // Return the forking job itself so ExecuteAsync can skip re-executing its body
            // while still fan-outing through ExecuteForkJoinAsync via skipFirstJobExecution.
            return currentJob;
        }

        var direct = definition.ResolveDirectTarget(currentJob);
        if (direct is not null)
            return direct;

        var router = definition.ResolveRouter(currentJob);
        if (router is not null)
            return await router.RouteAsync(execution.State, ct);

        return null;
    }

    public async IAsyncEnumerable<WorkflowEvent<TState>> StreamAsync<TState>(
        WorkflowDefinition<TState> definition,
        TState initialState,
        WorkflowStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= new WorkflowStreamOptions();
        var channel = Channel.CreateBounded<WorkflowEvent<TState>>(
            new BoundedChannelOptions(options.Capacity)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var execution = new WorkflowExecution<TState>(definition.Name, initialState, definition.Metadata);

        var task = Task.Run(async () =>
        {
            try
            {
                await ExecuteAsync(definition, execution, definition.EntryJob,
                    linkedCts.Token, events: channel.Writer);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, linkedCts.Token);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                yield return evt;
            }
        }
        finally
        {
            await linkedCts.CancelAsync();
            try { await task; }
            catch (Exception ex) { _logger.LogDebug(ex, "Workflow task faulted after cancellation — error already surfaced as WorkflowFaulted event"); }
        }
    }

    private async Task<WorkflowExecution<TState>> ExecuteAsync<TState>(
        WorkflowDefinition<TState> definition,
        WorkflowExecution<TState> execution,
        string? startJob,
        CancellationToken ct,
        bool skipFirstInterrupt = false,
        ChannelWriter<WorkflowEvent<TState>>? events = null,
        bool skipFirstJobExecution = false)
    {
        var totalSw = Stopwatch.StartNew();
        execution.Status = ExecutionStatus.Running;
        var currentJobName = startJob;

        LogWorkflowStarting(definition.Name, execution.Id);

        await using var trace = _tracer.StartTrace(
            definition.Name, execution.Id,
            new Dictionary<string, string>(execution.Metadata) { ["entry_job"] = startJob ?? string.Empty });

        try
        {
            while (currentJobName is not null && currentJobName != Workflow.EndMarker)
            {
                ct.ThrowIfCancellationRequested();

                if (!definition.Jobs.TryGetValue(currentJobName, out var descriptor))
                    throw new InvalidOperationException($"Job '{currentJobName}' is not defined in workflow '{definition.Name}'.");

                execution.CurrentJob = currentJobName;

                // --- Interrupt Before ---
                if (descriptor.Interrupt == InterruptMode.Before && !skipFirstInterrupt)
                {
                    if (_checkpointStore is null)
                        throw new InvalidOperationException(
                            $"Job '{currentJobName}' has InterruptBefore configured but no checkpoint store is available. " +
                            "Call UseCheckpointing() on the workflow builder.");

                    execution.Status = ExecutionStatus.Interrupted;
                    var interruptCheckpoint = Checkpoint<TState>.CreateInterrupt(execution, currentJobName, _checkpointTtl);
                    await _checkpointStore.SaveAsync(interruptCheckpoint, ct);

                    LogInterruptedBefore(definition.Name, execution.Id, currentJobName);

                    await EmitEventAsync(events, new Interrupted<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        JobName = currentJobName, State = execution.State
                    }, ct);

                    return execution;
                }
                skipFirstInterrupt = false;

                // When resuming after a fork, skip re-executing the forking job body
                // but still process its outgoing connections (fork fan-out).
                if (skipFirstJobExecution)
                {
                    skipFirstJobExecution = false;

                    // --- Fork/Join (resume path) ---
                    var resumeFork = definition.ResolveFork(currentJobName);
                    if (resumeFork is not null)
                    {
                        await EmitEventAsync(events, new ForkStarted<TState>
                        {
                            WorkflowName = definition.Name, ExecutionId = execution.Id,
                            Targets = resumeFork.Targets
                        }, ct);

                        currentJobName = await ExecuteForkJoinAsync(definition, resumeFork, execution, trace, ct);

                        await EmitEventAsync(events, new JoinCompleted<TState>
                        {
                            WorkflowName = definition.Name, ExecutionId = execution.Id,
                            Target = currentJobName, State = execution.State
                        }, ct);

                        await EmitEventAsync(events, new StateUpdated<TState>
                        {
                            WorkflowName = definition.Name, ExecutionId = execution.Id,
                            State = execution.State
                        }, ct);

                        continue;
                    }

                    // No fork on this job; fall through to normal routing.
                    currentJobName = await ResolveNextJobAsync(definition, currentJobName, execution.State, ct);
                    continue;
                }

                await using var jobSpan = trace.StartSpan(currentJobName, SpanKind.Job);
                jobSpan.SetAttribute("workflow", definition.Name);
                jobSpan.SetAttribute("execution_id", execution.Id);

                // H-7: capture previous ambient values so they can be restored in the
                // finally block regardless of how the job exits (success, fault, interrupt).
                var prevTrace = WorkflowTraceContext.Value;
                var prevUsage = TokenUsageCapture.Current.Value;
                var usageAccumulator = new UsageAccumulator();

                WorkflowTraceContext.Value = new TraceInfo(
                    definition.Name, execution.Id, currentJobName, trace, jobSpan,
                    _storeCompletions);
                TokenUsageCapture.Current.Value = usageAccumulator;

                if (descriptor.OnEnter is not null)
                    await descriptor.OnEnter(execution.State);

                var jobSw = Stopwatch.StartNew();

                using var timeoutCts = descriptor.Timeout.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                    : null;
                timeoutCts?.CancelAfter(descriptor.Timeout!.Value);
                var jobCt = timeoutCts?.Token ?? ct;

                LogJobStarting(currentJobName, definition.Name, execution.Id);

                await EmitEventAsync(events, new JobStarted<TState>
                {
                    WorkflowName = definition.Name, ExecutionId = execution.Id,
                    JobName = currentJobName
                }, ct);

                try
                {
                    execution.State = await ExecuteJobWithMiddlewareAsync(
                        descriptor, currentJobName, execution.State, jobCt);
                    jobSw.Stop();

                    if (descriptor.OnExit is not null)
                        await descriptor.OnExit(execution.State);

                    execution.RecordJobExecution(JobExecution.FromStopwatch(currentJobName, jobSw, true));

                    LogJobCompleted(currentJobName, jobSw.ElapsedMilliseconds);

                    await EmitEventAsync(events, new JobCompleted<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        JobName = currentJobName, Duration = jobSw.Elapsed,
                        State = execution.State
                    }, ct);

                    await EmitEventAsync(events, new StateUpdated<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        State = execution.State
                    }, ct);
                }
                catch (SubFlowInterruptedException)
                {
                    jobSw.Stop();
                    jobSpan.SetAttribute("subflow.interrupted", "true");

                    if (_checkpointStore is null)
                        throw new InvalidOperationException(
                            $"SubFlow '{currentJobName}' was interrupted but no checkpoint store is available. " +
                            "Call UseCheckpointing() on the workflow builder.");

                    execution.Status = ExecutionStatus.Interrupted;
                    var interruptCheckpoint = Checkpoint<TState>.CreateInterrupt(
                        execution, currentJobName, _checkpointTtl);
                    await _checkpointStore.SaveAsync(interruptCheckpoint, ct);

                    LogInterruptedBySubflow(definition.Name, execution.Id, currentJobName);

                    await EmitEventAsync(events, new Interrupted<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        JobName = currentJobName, State = execution.State
                    }, ct);

                    return execution;
                }
                catch (OperationCanceledException) when (
                    timeoutCts is not null && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    jobSw.Stop();
                    var timeoutMsg = $"Job '{currentJobName}' timed out after {descriptor.Timeout!.Value.TotalSeconds:F1}s.";
                    var timeoutEx = new TimeoutException(timeoutMsg);
                    jobSpan.RecordError(timeoutEx);
                    execution.RecordJobExecution(JobExecution.FromStopwatch(currentJobName, jobSw, false, timeoutMsg));
                    LogJobTimedOut(currentJobName, descriptor.Timeout!.Value.TotalSeconds);
                    await InvokeFaultHandlersAsync(descriptor, definition, currentJobName, execution.State, timeoutEx);
                    throw timeoutEx;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    jobSw.Stop();
                    jobSpan.RecordError(ex);
                    execution.RecordJobExecution(JobExecution.FromStopwatch(currentJobName, jobSw, false, ex.Message));
                    LogJobFailed(ex, currentJobName, ex.Message);
                    await InvokeFaultHandlersAsync(descriptor, definition, currentJobName, execution.State, ex);
                    throw;
                }
                finally
                {
                    // H-7: restore ambient context so stale trace/usage cannot leak into
                    // async continuations that escape this job's execution scope.
                    WorkflowTraceContext.Value = prevTrace;
                    TokenUsageCapture.Current.Value = prevUsage;
                }

                if (_checkpointStore is not null)
                {
                    var checkpoint = Checkpoint<TState>.Create(execution, _checkpointTtl);
                    await _checkpointStore.SaveAsync(checkpoint, ct);
                }

                // --- Budget enforcement ---
                var jobUsage = usageAccumulator.Usage;
                if (jobUsage.TotalTokens > 0)
                {
                    execution.CumulativeUsage = execution.CumulativeUsage.Add(jobUsage);

                    if (definition.Budget is { } budget)
                    {
                        // Prefer model-specific per-call cost (from CapabilityModelRouter profiles).
                        // Falls back to flat BudgetConfig rates when model cost isn't available.
                        execution.EstimatedCost += usageAccumulator.HasModelBasedCost
                            ? usageAccumulator.AccumulatedCost
                            : budget.EstimateCost(jobUsage);

                        if (execution.EstimatedCost > budget.MaxCost)
                        {
                            totalSw.Stop();
                            execution.Status = ExecutionStatus.BudgetExceeded;
                            execution.CurrentJob = currentJobName;
                            execution.Result = WorkflowResult<TState>.Failed(
                                execution.State, totalSw.Elapsed, execution.History,
                                $"Cost budget exceeded: estimated {execution.EstimatedCost:F6} > limit {budget.MaxCost:F6}");

                            // 4.7: Persist the budget-exceeded checkpoint so the stored
                            // state is consistent with the terminal execution status.
                            if (_checkpointStore is not null)
                            {
                                var budgetCheckpoint = Checkpoint<TState>.Create(execution, _checkpointTtl);
                                await _checkpointStore.SaveAsync(budgetCheckpoint, ct);
                            }

                            await EmitEventAsync(events, new BudgetExceeded<TState>
                            {
                                WorkflowName = definition.Name,
                                ExecutionId = execution.Id,
                                EstimatedCost = execution.EstimatedCost,
                                Budget = budget.MaxCost,
                                CumulativeUsage = execution.CumulativeUsage
                            }, ct);

                            return execution;
                        }
                    }
                }

                // --- Interrupt After ---
                if (descriptor.Interrupt == InterruptMode.After)
                {
                    if (_checkpointStore is null)
                        throw new InvalidOperationException(
                            $"Job '{currentJobName}' has InterruptAfter configured but no checkpoint store is available. " +
                            "Call UseCheckpointing() on the workflow builder.");

                    execution.Status = ExecutionStatus.Interrupted;
                    // Re-save checkpoint with interrupted status (normal checkpoint was already saved above)
                    var interruptCheckpoint = Checkpoint<TState>.Create(execution, _checkpointTtl);
                    await _checkpointStore.SaveAsync(interruptCheckpoint, ct);

                    LogInterruptedAfter(definition.Name, execution.Id, currentJobName);

                    await EmitEventAsync(events, new Interrupted<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        JobName = currentJobName, State = execution.State
                    }, ct);

                    return execution;
                }

                // --- Fork/Join ---
                var forkConn = definition.ResolveFork(currentJobName);
                if (forkConn is not null)
                {
                    await EmitEventAsync(events, new ForkStarted<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        Targets = forkConn.Targets
                    }, ct);

                    currentJobName = await ExecuteForkJoinAsync(definition, forkConn, execution, trace, ct);

                    await EmitEventAsync(events, new JoinCompleted<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        Target = currentJobName, State = execution.State
                    }, ct);

                    await EmitEventAsync(events, new StateUpdated<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        State = execution.State
                    }, ct);

                    continue;
                }

                // --- Loop ---
                var loopConn = definition.ResolveLoop(currentJobName);
                if (loopConn is not null)
                {
                    var iteration = execution.IncrementLoopCounter(currentJobName);

                    LoopExitReason? exitReason = null;

                    if (loopConn.Until(execution.State))
                        exitReason = LoopExitReason.ConditionMet;
                    else if (iteration >= loopConn.MaxIterations)
                        exitReason = LoopExitReason.MaxIterationsReached;

                    if (exitReason.HasValue)
                    {
                        var completedIterations = iteration;
                        execution.ResetLoopCounter(currentJobName);

                        LogLoopExited(definition.Name, execution.Id, currentJobName, completedIterations, exitReason.Value.ToString());

                        await EmitEventAsync(events, new LoopExited<TState>
                        {
                            WorkflowName = definition.Name,
                            ExecutionId = execution.Id,
                            LoopFrom = currentJobName,
                            LoopTarget = loopConn.LoopTarget,
                            IterationsCompleted = completedIterations,
                            Reason = exitReason.Value
                        }, ct);

                        currentJobName = loopConn.ExitTarget;
                    }
                    else
                    {
                        jobSpan.SetAttribute("loop.iteration", iteration.ToString());
                        currentJobName = loopConn.LoopTarget;
                    }
                    continue;
                }

                currentJobName = await ResolveNextJobAsync(definition, currentJobName, execution.State, ct);
            }

            totalSw.Stop();
            execution.Status = ExecutionStatus.Completed;
            execution.CurrentJob = null;
            execution.Result = WorkflowResult<TState>.Succeeded(execution.State, totalSw.Elapsed, execution.History);

            LogWorkflowCompleted(definition.Name, execution.Id, totalSw.ElapsedMilliseconds, execution.History.Count);

            await EmitEventAsync(events, new WorkflowCompleted<TState>
            {
                WorkflowName = definition.Name, ExecutionId = execution.Id,
                Result = execution.Result
            }, ct);

            if (_checkpointStore is not null)
                await _checkpointStore.DeleteAsync(execution.Id, ct);
        }
        catch (OperationCanceledException)
        {
            totalSw.Stop();
            execution.Status = ExecutionStatus.Cancelled;
            execution.Result = WorkflowResult<TState>.Cancelled(
                execution.State, totalSw.Elapsed, execution.History);
            LogWorkflowCancelled(definition.Name, execution.Id);
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            execution.Status = ExecutionStatus.Faulted;
            execution.Result = WorkflowResult<TState>.Failed(
                execution.State, totalSw.Elapsed, execution.History, ex.Message, ex);
            LogWorkflowFaulted(ex, definition.Name, execution.Id, ex.Message);

            await EmitEventAsync(events, new WorkflowFaulted<TState>
            {
                WorkflowName = definition.Name, ExecutionId = execution.Id,
                Exception = ex, State = execution.State
            });
        }

        return execution;
    }

    private async Task<TState> ExecuteJobWithMiddlewareAsync<TState>(
        JobDescriptor<TState> descriptor,
        string jobName,
        TState state,
        CancellationToken ct)
    {
        if (_middlewares.Count == 0)
            return await descriptor.Job.ExecuteAsync(state, ct);

        Func<Task<TState>> pipeline = () => descriptor.Job.ExecuteAsync(state, ct);

        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = () => InvokeMiddlewareAsync(middleware, jobName, state, next, ct);
        }

        return await pipeline();
    }

    /// <summary>
    /// Invokes per-job <see cref="JobDescriptor{TState}.OnFault"/> and workflow-level
    /// <see cref="WorkflowDefinition{TState}.OnError"/> handlers (in that order).
    /// Exceptions thrown by handlers are logged but do not replace the original fault.
    /// </summary>
    private async Task InvokeFaultHandlersAsync<TState>(
        JobDescriptor<TState> descriptor,
        WorkflowDefinition<TState> definition,
        string jobName,
        TState state,
        Exception exception)
    {
        if (descriptor.OnFault is not null)
        {
            try
            {
                await descriptor.OnFault(state, exception);
            }
            catch (Exception handlerEx)
            {
                _logger.LogError(handlerEx,
                    "OnFault handler for job '{JobName}' threw an exception", jobName);
            }
        }

        if (definition.OnError is not null)
        {
            try
            {
                await definition.OnError(state, jobName, exception);
            }
            catch (Exception handlerEx)
            {
                _logger.LogError(handlerEx,
                    "OnError handler threw an exception for job '{JobName}'", jobName);
            }
        }
    }

    private static async Task<TState> InvokeMiddlewareAsync<TState>(
        IWorkflowJobMiddleware<object> middleware,
        string jobName,
        TState state,
        Func<Task<TState>> next,
        CancellationToken ct)
    {
        var result = await middleware.InvokeAsync(
            jobName,
            state!,
            async () => (object)(await next())!,
            ct);
        return (TState)result;
    }

    private static async Task<string?> ResolveNextJobAsync<TState>(
        WorkflowDefinition<TState> definition,
        string currentJob,
        TState state,
        CancellationToken ct = default)
    {
        var direct = definition.ResolveDirectTarget(currentJob);
        if (direct is not null)
            return direct;

        var router = definition.ResolveRouter(currentJob);
        if (router is not null)
            return await router.RouteAsync(state, ct);

        return null;
    }

    // ------------------------------------------------------------------
    //  Fork / Join — parallel branch execution
    // ------------------------------------------------------------------

    private sealed record BranchResult<TState>(string FinalJob, TState FinalState, List<JobExecution> History);

    private async Task<string> ExecuteForkJoinAsync<TState>(
        WorkflowDefinition<TState> definition,
        ForkConnection fork,
        WorkflowExecution<TState> execution,
        ITrace trace,
        CancellationToken ct)
    {
        await using var forkSpan = trace.StartSpan("fork", SpanKind.Job);
        forkSpan.SetAttribute("fork.targets", string.Join(",", fork.Targets));
        forkSpan.SetAttribute("fork.mode", fork.Mode.ToString());

        LogForkStarting(definition.Name, execution.Id, string.Join(", ", fork.Targets), fork.Mode.ToString());

        using var forkCts = fork.Mode == ForkMode.FailFast
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        var branchCt = forkCts?.Token ?? ct;

        var branchTasks = fork.Targets.Select(target =>
            RunBranchAsync(definition, target, execution.State, execution.Id,
                forkSpan, branchCt, failFastCts: forkCts)).ToList();

        BranchResult<TState>[] results;

        if (fork.Mode == ForkMode.FailFast)
        {
            try
            {
                results = await Task.WhenAll(branchTasks);
            }
            catch
            {
                // Extract the root-cause exception(s), filtering out cancellations from sibling branches
                var faults = branchTasks
                    .Where(t => t.IsFaulted)
                    .SelectMany(t => t.Exception!.InnerExceptions)
                    .Where(e => e is not OperationCanceledException)
                    .ToList();

                if (faults.Count == 1) throw faults[0];
                if (faults.Count > 1) throw new AggregateException("Fork branch(es) faulted.", faults);
                throw; // Only cancellations
            }
        }
        else
        {
            try
            {
                results = await Task.WhenAll(branchTasks);
            }
            catch
            {
                // BestEffort: collect whatever succeeded
                results = branchTasks
                    .Where(t => t.IsCompletedSuccessfully)
                    .Select(t => t.Result)
                    .ToArray();

                if (results.Length == 0)
                {
                    var faults = branchTasks
                        .Where(t => t.IsFaulted)
                        .SelectMany(t => t.Exception!.InnerExceptions)
                        .ToList();
                    throw new AggregateException("All fork branches faulted.", faults);
                }

                LogForkPartialSuccess(definition.Name, execution.Id, results.Length, fork.Targets.Count);
            }
        }

        // Match branch endpoints to a JoinDescriptor
        var branchEndpoints = results.Select(r => r.FinalJob).ToHashSet();
        var join = definition.Joins.FirstOrDefault(j => j.Sources.ToHashSet().SetEquals(branchEndpoints));

        // BestEffort: allow partial matches when some branches failed
        if (join is null && fork.Mode == ForkMode.BestEffort)
            join = definition.Joins.FirstOrDefault(j => branchEndpoints.IsSubsetOf(j.Sources.ToHashSet()));

        if (join is null)
            throw new InvalidOperationException(
                $"No matching Join found for branch endpoints: [{string.Join(", ", branchEndpoints)}]. " +
                $"Defined joins: {string.Join("; ", definition.Joins.Select(j => $"[{string.Join(", ", j.Sources)}] ? {j.Target}"))}");

        // Order states to match join sources declaration order
        var orderedStates = join.Sources
            .Where(source => results.Any(r => r.FinalJob == source))
            .Select(source => results.First(r => r.FinalJob == source).FinalState)
            .ToArray();

        execution.State = join.Merge(orderedStates);

        // Record all branch histories
        foreach (var result in results)
        {
            foreach (var jobExec in result.History)
                execution.RecordJobExecution(jobExec);
        }

        LogJoinCompleted(definition.Name, execution.Id, results.Length, join.Target);

        forkSpan.SetAttribute("fork.join_target", join.Target);
        return join.Target;
    }

    private async Task<BranchResult<TState>> RunBranchAsync<TState>(
        WorkflowDefinition<TState> definition,
        string startJob,
        TState branchState,
        string executionId,
        ISpan parentSpan,
        CancellationToken ct,
        CancellationTokenSource? failFastCts)
    {
        await using var branchSpan = parentSpan.StartSpan($"branch:{startJob}", SpanKind.Job);
        try
        {
            return await ExecuteBranchAsync(definition, startJob, branchState, executionId, branchSpan, ct);
        }
        catch (Exception ex)
        {
            branchSpan.RecordError(ex);
            if (failFastCts is not null)
                await failFastCts.CancelAsync();
            throw;
        }
    }

    private async Task<BranchResult<TState>> ExecuteBranchAsync<TState>(
        WorkflowDefinition<TState> definition,
        string startJob,
        TState state,
        string executionId,
        ISpan branchSpan,
        CancellationToken ct)
    {
        var history = new List<JobExecution>();
        var currentJobName = startJob;
        var lastCompletedJob = startJob;

        while (currentJobName is not null && currentJobName != Workflow.EndMarker)
        {
            ct.ThrowIfCancellationRequested();

            if (!definition.Jobs.TryGetValue(currentJobName, out var descriptor))
                throw new InvalidOperationException(
                    $"Job '{currentJobName}' is not defined in workflow '{definition.Name}'.");

            await using var jobSpan = branchSpan.StartSpan(currentJobName, SpanKind.Job);
            jobSpan.SetAttribute("workflow", definition.Name);
            jobSpan.SetAttribute("execution_id", executionId);
            jobSpan.SetAttribute("fork.branch", "true");

            WorkflowTraceContext.Value = new TraceInfo(
                definition.Name, executionId, currentJobName,
                CurrentSpan: jobSpan, StoreCompletions: _storeCompletions);

            if (descriptor.OnEnter is not null)
                await descriptor.OnEnter(state);

            var jobSw = Stopwatch.StartNew();

            using var timeoutCts = descriptor.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            timeoutCts?.CancelAfter(descriptor.Timeout!.Value);
            var jobCt = timeoutCts?.Token ?? ct;

            LogBranchJobStarting(currentJobName, definition.Name);

            try
            {
                state = await ExecuteJobWithMiddlewareAsync(descriptor, currentJobName, state, jobCt);
                jobSw.Stop();

                if (descriptor.OnExit is not null)
                    await descriptor.OnExit(state);

                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, true));

                LogBranchJobCompleted(currentJobName, jobSw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (
                timeoutCts is not null && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                jobSw.Stop();
                var timeoutMsg = $"Job '{currentJobName}' timed out after {descriptor.Timeout!.Value.TotalSeconds:F1}s.";
                var timeoutEx = new TimeoutException(timeoutMsg);
                jobSpan.RecordError(timeoutEx);
                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, false, timeoutMsg));
                await InvokeFaultHandlersAsync(descriptor, definition, currentJobName, state, timeoutEx);
                throw timeoutEx;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                jobSw.Stop();
                jobSpan.RecordError(ex);
                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, false, ex.Message));
                await InvokeFaultHandlersAsync(descriptor, definition, currentJobName, state, ex);
                throw;
            }

            lastCompletedJob = currentJobName;
            currentJobName = await ResolveNextJobAsync(definition, currentJobName, state);
        }

        return new BranchResult<TState>(lastCompletedJob, state, history);
    }

    private static async ValueTask EmitEventAsync<TState>(
        ChannelWriter<WorkflowEvent<TState>>? events,
        WorkflowEvent<TState> evt,
        CancellationToken ct = default)
    {
        if (events is null) return;
        try { await events.WriteAsync(evt, ct); }
        catch (ChannelClosedException) { }
        catch (OperationCanceledException) { }
    }

    // -- Source-generated structured log methods ----------------------

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} [{ExecutionId}] starting")]
    private partial void LogWorkflowStarting(string workflowName, string executionId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} [{ExecutionId}] interrupted before job {JobName}")]
    private partial void LogInterruptedBefore(string workflowName, string executionId, string jobName);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} [{ExecutionId}] interrupted by subflow at job {JobName}")]
    private partial void LogInterruptedBySubflow(string workflowName, string executionId, string jobName);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} [{ExecutionId}] interrupted after job {JobName}")]
    private partial void LogInterruptedAfter(string workflowName, string executionId, string jobName);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Job {JobName} starting in workflow {WorkflowName} [{ExecutionId}]")]
    private partial void LogJobStarting(string jobName, string workflowName, string executionId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Job {JobName} completed in {DurationMs}ms")]
    private partial void LogJobCompleted(string jobName, long durationMs);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Job {JobName} timed out after {TimeoutSeconds}s")]
    private partial void LogJobTimedOut(string jobName, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Job {JobName} failed: {Error}")]
    private partial void LogJobFailed(Exception exception, string jobName, string error);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} [{ExecutionId}] completed in {DurationMs}ms ({JobCount} jobs)")]
    private partial void LogWorkflowCompleted(string workflowName, string executionId, long durationMs, int jobCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Workflow {WorkflowName} [{ExecutionId}] was cancelled")]
    private partial void LogWorkflowCancelled(string workflowName, string executionId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Workflow {WorkflowName} [{ExecutionId}] faulted: {Error}")]
    private partial void LogWorkflowFaulted(Exception exception, string workflowName, string executionId, string error);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} [{ExecutionId}] forking to [{Targets}] ({Mode})")]
    private partial void LogForkStarting(string workflowName, string executionId, string targets, string mode);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Workflow {WorkflowName} [{ExecutionId}] fork completed with {SucceededCount}/{TotalCount} branches")]
    private partial void LogForkPartialSuccess(string workflowName, string executionId, int succeededCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} [{ExecutionId}] joined {BranchCount} branches at {JoinTarget}")]
    private partial void LogJoinCompleted(string workflowName, string executionId, int branchCount, string joinTarget);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Branch job {JobName} starting in workflow {WorkflowName}")]
    private partial void LogBranchJobStarting(string jobName, string workflowName);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Branch job {JobName} completed in {DurationMs}ms")]
    private partial void LogBranchJobCompleted(string jobName, long durationMs);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} [{ExecutionId}] loop at {LoopFrom} exited after {Iterations} iteration(s): {Reason}")]
    private partial void LogLoopExited(string workflowName, string executionId, string loopFrom, int iterations, string reason);
}
