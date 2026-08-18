using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Budget;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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

using Ananke.Orchestration.Usage;

namespace Ananke.Orchestration.Execution;

public sealed partial class WorkflowRunner : IWorkflowRunner
{
    private readonly ICheckpointStore? _checkpointStore;
    private readonly IReadOnlyList<IWorkflowJobMiddleware<object>> _middlewares;
    private readonly IWorkflowTracer _tracer;
    private readonly bool _storeCompletions;
    private readonly ILogger<WorkflowRunner> _logger;
    private readonly TimeSpan _checkpointTtl;
    private readonly TimeProvider _timeProvider;

    public WorkflowRunner(
        ICheckpointStore? checkpointStore = null,
        IEnumerable<IWorkflowJobMiddleware<object>>? middlewares = null,
        IWorkflowTracer? tracer = null,
        bool storeCompletions = false,
        ILoggerFactory? loggerFactory = null,
        TimeSpan? checkpointTtl = null,
        TimeProvider? timeProvider = null,
        IUsageRecorder? usageRecorder = null)
    {
        _usageRecorder = usageRecorder;
        _checkpointStore = checkpointStore;
        _middlewares = middlewares?.ToList() ?? [];
        _tracer = tracer ?? NullTracer.Instance;
        _storeCompletions = storeCompletions;
        _logger = loggerFactory?.CreateLogger<WorkflowRunner>()
            ?? NullLogger<WorkflowRunner>.Instance;
        _checkpointTtl = checkpointTtl ?? TimeSpan.FromDays(7);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WorkflowExecution<TState>> RunAsync<TState>(
        WorkflowDefinition<TState> definition,
        TState initialState,
        CancellationToken ct = default)
    {
        var execution = new WorkflowExecution<TState>(definition.Name, initialState, definition.Metadata);
        return await ExecuteAsync(definition, execution, definition.EntryJob, ct).ConfigureAwait(false);
    }

    public async Task<WorkflowExecution<TState>> ResumeAsync<TState>(
        WorkflowDefinition<TState> definition,
        Checkpoint<TState> checkpoint,
        CancellationToken ct = default)
    {
        var execution = WorkflowExecution<TState>.FromCheckpoint(checkpoint);

        if (checkpoint.InterruptedBeforeJob is not null)
            return await ExecuteAsync(definition, execution, checkpoint.InterruptedBeforeJob, ct, skipFirstInterrupt: true).ConfigureAwait(false);

        var nextJob = await ResolveResumeTargetAsync(definition, checkpoint.CurrentJob, execution, ct).ConfigureAwait(false);
        var skipBody = definition.ResolveFork(checkpoint.CurrentJob) is not null;
        return await ExecuteAsync(definition, execution, nextJob, ct, skipFirstJobExecution: skipBody).ConfigureAwait(false);
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
            return await ExecuteAsync(definition, execution, checkpoint.InterruptedBeforeJob, ct, skipFirstInterrupt: true).ConfigureAwait(false);

        var nextJob = await ResolveResumeTargetAsync(definition, checkpoint.CurrentJob, execution, ct).ConfigureAwait(false);
        var skipBody = definition.ResolveFork(checkpoint.CurrentJob) is not null;
        return await ExecuteAsync(definition, execution, nextJob, ct, skipFirstJobExecution: skipBody).ConfigureAwait(false);
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
            return await router.RouteAsync(execution.State, ct).ConfigureAwait(false);

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
                    linkedCts.Token, events: channel.Writer).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, linkedCts.Token);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
            try { await task.ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Workflow task faulted after cancellation — error already surfaced as WorkflowFaulted event"); }
        }
    }

    private readonly IUsageRecorder? _usageRecorder;

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

        // One recorder per execution. BeginScope does not nest, so a sub-workflow's runner
        // inherits its parent's — which is what makes child spend visible to the parent's
        // budget. The baseline lets this execution report its own totals out of a recorder
        // it may be sharing.
        // Injected wins: the caller named it. Then ambient — a sub-workflow's runner is built
        // without one, so it inherits its parent's and the parent's budget sees the child's
        // spend. Only then the per-run default.
        var usageRecorder = _usageRecorder ?? UsageRecording.Current ?? new InMemoryUsageRecorder();
        using var usageScope = UsageRecording.BeginScope(usageRecorder);

        LogWorkflowStarting(definition.Name, execution.Id);

        await using var trace = _tracer.StartTrace(
            definition.Name, execution.Id,
            new Dictionary<string, string>(execution.Metadata) { ["entry_job"] = startJob ?? string.Empty });

        try
        {
            // Read inside the try: a caller who cancels before the run starts must still get a
            // Cancelled result, not an exception escaping the runner.
            var usageBaseline = await usageRecorder.ReadAsync(ct).ConfigureAwait(false);

            // One gate per execution, shared with every fork branch so the two paths cannot
            // drift on the arithmetic — which is the class of bug this whole ADR is about.
            var budgetGate = new BudgetGate(usageRecorder, usageBaseline, definition.Budget);

            // Terminating for budget happens from two places — the per-job check, and a fork
            // whose branches stopped — so it lives in one place rather than being written twice.
            async Task TerminateForBudgetAsync(BudgetVerdict v, string? atJob)
            {
                var isPeriod = v.Limit == BudgetLimitKind.Period;
                var spent = isPeriod ? v.PeriodCost : v.RunCost;
                var ceiling = budgetGate.LimitFor(v.Limit);

                totalSw.Stop();
                execution.Status = ExecutionStatus.BudgetExceeded;
                execution.CurrentJob = atJob;
                execution.EstimatedCost = v.RunCost;
                execution.Result = WorkflowResult<TState>.Failed(
                    execution.State, totalSw.Elapsed, execution.History,
                    isPeriod
                        ? $"Period cost budget exceeded: {spent:F6} spent this period > limit {ceiling:F6}"
                        : $"Cost budget exceeded: estimated {spent:F6} > limit {ceiling:F6}",
                    branchOutcomes: execution.BranchOutcomes);

                // 4.7: Persist the budget-exceeded checkpoint so the stored state is
                // consistent with the terminal execution status.
                if (_checkpointStore is not null)
                {
                    var budgetCheckpoint = Checkpoint<TState>.Create(execution, _checkpointTtl, _timeProvider);
                    await _checkpointStore.SaveAsync(budgetCheckpoint, ct).ConfigureAwait(false);
                }

                await EmitEventAsync(events, new BudgetExceeded<TState>
                {
                    WorkflowName = definition.Name,
                    ExecutionId = execution.Id,
                    EstimatedCost = spent,
                    Budget = ceiling,
                    CumulativeUsage = execution.CumulativeUsage
                }, ct).ConfigureAwait(false);
            }

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
                    var interruptCheckpoint = Checkpoint<TState>.CreateInterrupt(execution, currentJobName, _checkpointTtl, _timeProvider);
                    await _checkpointStore.SaveAsync(interruptCheckpoint, ct).ConfigureAwait(false);

                    LogInterruptedBefore(definition.Name, execution.Id, currentJobName);

                    await EmitEventAsync(events, new Interrupted<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        JobName = currentJobName,
                        State = execution.State
                    }, ct).ConfigureAwait(false);

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
                            WorkflowName = definition.Name,
                            ExecutionId = execution.Id,
                            Targets = resumeFork.Targets
                        }, ct).ConfigureAwait(false);

                        var joinTarget = await ExecuteForkJoinAsync(
                            definition, resumeFork, execution, trace, events, budgetGate, ct).ConfigureAwait(false);

                        // null: a branch reached the budget. Totals already include the fork's
                        // spend, so re-evaluate and end the run rather than joining.
                        if (joinTarget is null)
                        {
                            var forkVerdict = await budgetGate.EvaluateAsync(ct).ConfigureAwait(false);
                            execution.CumulativeUsage = forkVerdict.RunTotals.Usage;
                            await TerminateForBudgetAsync(forkVerdict, currentJobName).ConfigureAwait(false);
                            return execution;
                        }

                        currentJobName = joinTarget;

                        await EmitEventAsync(events, new JoinCompleted<TState>
                        {
                            WorkflowName = definition.Name,
                            ExecutionId = execution.Id,
                            Target = currentJobName,
                            State = execution.State
                        }, ct).ConfigureAwait(false);

                        await EmitEventAsync(events, new StateUpdated<TState>
                        {
                            WorkflowName = definition.Name,
                            ExecutionId = execution.Id,
                            State = execution.State
                        }, ct).ConfigureAwait(false);

                        continue;
                    }

                    // No fork on this job; fall through to normal routing.
                    currentJobName = await ResolveNextJobAsync(definition, currentJobName, execution.State, ct).ConfigureAwait(false);
                    continue;
                }

                await using var jobSpan = trace.StartSpan(currentJobName, SpanKind.Job);
                jobSpan.SetAttribute("workflow", definition.Name);
                jobSpan.SetAttribute("execution_id", execution.Id);

                // H-7: capture the previous ambient value so it can be restored in the
                // finally block regardless of how the job exits (success, fault, interrupt).
                // The usage recorder is deliberately NOT part of this: it is scoped once per
                // execution, not per job, which is what lets branches and sub-workflows record
                // into it instead of silently getting their own (ADR-arch-028 D7).
                var prevTrace = WorkflowTraceContext.Value;

                WorkflowTraceContext.Value = new TraceInfo(
                    definition.Name, execution.Id, currentJobName, trace, jobSpan,
                    _storeCompletions);

                if (descriptor.OnEnter is not null)
                    await descriptor.OnEnter(execution.State).ConfigureAwait(false);

                var jobSw = Stopwatch.StartNew();

                using var timeoutCts = descriptor.Timeout.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                    : null;
                timeoutCts?.CancelAfter(descriptor.Timeout!.Value);
                var jobCt = timeoutCts?.Token ?? ct;

                LogJobStarting(currentJobName, definition.Name, execution.Id);

                await EmitEventAsync(events, new JobStarted<TState>
                {
                    WorkflowName = definition.Name,
                    ExecutionId = execution.Id,
                    JobName = currentJobName
                }, ct).ConfigureAwait(false);

                try
                {
                    execution.State = await ExecuteJobWithMiddlewareAsync(
                        descriptor, currentJobName, execution.State, jobCt).ConfigureAwait(false);
                    jobSw.Stop();

                    if (descriptor.OnExit is not null)
                        await descriptor.OnExit(execution.State).ConfigureAwait(false);

                    execution.RecordJobExecution(JobExecution.FromStopwatch(currentJobName, jobSw, true));

                    LogJobCompleted(currentJobName, jobSw.ElapsedMilliseconds);

                    await EmitEventAsync(events, new JobCompleted<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        JobName = currentJobName,
                        Duration = jobSw.Elapsed,
                        State = execution.State
                    }, ct).ConfigureAwait(false);

                    await EmitEventAsync(events, new StateUpdated<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        State = execution.State
                    }, ct).ConfigureAwait(false);
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
                        execution, currentJobName, _checkpointTtl, _timeProvider);
                    await _checkpointStore.SaveAsync(interruptCheckpoint, ct).ConfigureAwait(false);

                    LogInterruptedBySubflow(definition.Name, execution.Id, currentJobName);

                    await EmitEventAsync(events, new Interrupted<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        JobName = currentJobName,
                        State = execution.State
                    }, ct).ConfigureAwait(false);

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
                    await InvokeFaultHandlersAsync(descriptor, definition, currentJobName, execution.State, timeoutEx).ConfigureAwait(false);
                    throw timeoutEx;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    jobSw.Stop();
                    jobSpan.RecordError(ex);
                    execution.RecordJobExecution(JobExecution.FromStopwatch(currentJobName, jobSw, false, ex.Message));
                    LogJobFailed(ex, currentJobName, ex.Message);
                    await InvokeFaultHandlersAsync(descriptor, definition, currentJobName, execution.State, ex).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    // H-7: restore ambient context so a stale trace cannot leak into async
                    // continuations that escape this job's execution scope.
                    WorkflowTraceContext.Value = prevTrace;
                }

                if (_checkpointStore is not null)
                {
                    var checkpoint = Checkpoint<TState>.Create(execution, _checkpointTtl, _timeProvider);
                    await _checkpointStore.SaveAsync(checkpoint, ct).ConfigureAwait(false);
                }

                // --- Budget enforcement ---
                // Totals come from the recorder, as this execution's delta since it began.
                // The baseline matters for a sub-workflow, which inherits its parent's recorder:
                // without it the child would report the parent's spend as its own. Assignment,
                // not accumulation — summing per-job deltas is not well defined once fork
                // branches record concurrently (ADR-arch-028 D6/D7).
                var verdict = await budgetGate.EvaluateAsync(ct).ConfigureAwait(false);
                if (verdict.RunTotals.Usage.TotalTokens > 0)
                    execution.CumulativeUsage = verdict.RunTotals.Usage;

                if (budgetGate.HasBudget)
                    execution.EstimatedCost = verdict.RunCost;

                if (verdict.State == BudgetState.Warning)
                {
                    var warnSpent = verdict.Limit == BudgetLimitKind.Period ? verdict.PeriodCost : verdict.RunCost;
                    var warnAt = budgetGate.WarnThresholdFor(verdict.Limit);

                    LogBudgetWarning(definition.Name, execution.Id, warnSpent, warnAt);

                    await EmitEventAsync(events, new BudgetWarning<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        EstimatedCost = warnSpent,
                        WarnAtCost = warnAt,
                        Budget = budgetGate.LimitFor(verdict.Limit),
                        CumulativeUsage = execution.CumulativeUsage
                    }, ct).ConfigureAwait(false);
                }
                else if (verdict.State == BudgetState.Exceeded)
                {
                    await TerminateForBudgetAsync(verdict, currentJobName).ConfigureAwait(false);
                    return execution;
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
                    var interruptCheckpoint = Checkpoint<TState>.Create(execution, _checkpointTtl, _timeProvider);
                    await _checkpointStore.SaveAsync(interruptCheckpoint, ct).ConfigureAwait(false);

                    LogInterruptedAfter(definition.Name, execution.Id, currentJobName);

                    await EmitEventAsync(events, new Interrupted<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        JobName = currentJobName,
                        State = execution.State
                    }, ct).ConfigureAwait(false);

                    return execution;
                }

                // --- Fork/Join ---
                var forkConn = definition.ResolveFork(currentJobName);
                if (forkConn is not null)
                {
                    await EmitEventAsync(events, new ForkStarted<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        Targets = forkConn.Targets
                    }, ct).ConfigureAwait(false);

                    var joinTarget = await ExecuteForkJoinAsync(
                        definition, forkConn, execution, trace, events, budgetGate, ct).ConfigureAwait(false);

                    // null: a branch reached the budget. Totals already include the fork's
                    // spend, so re-evaluate and end the run rather than joining.
                    if (joinTarget is null)
                    {
                        var forkVerdict = await budgetGate.EvaluateAsync(ct).ConfigureAwait(false);
                        execution.CumulativeUsage = forkVerdict.RunTotals.Usage;
                        await TerminateForBudgetAsync(forkVerdict, currentJobName).ConfigureAwait(false);
                        return execution;
                    }

                    currentJobName = joinTarget;

                    await EmitEventAsync(events, new JoinCompleted<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        Target = currentJobName,
                        State = execution.State
                    }, ct).ConfigureAwait(false);

                    await EmitEventAsync(events, new StateUpdated<TState>
                    {
                        WorkflowName = definition.Name,
                        ExecutionId = execution.Id,
                        State = execution.State
                    }, ct).ConfigureAwait(false);

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
                        }, ct).ConfigureAwait(false);

                        currentJobName = loopConn.ExitTarget;
                    }
                    else
                    {
                        jobSpan.SetAttribute("loop.iteration", iteration.ToString());
                        currentJobName = loopConn.LoopTarget;
                    }
                    continue;
                }

                currentJobName = await ResolveNextJobAsync(definition, currentJobName, execution.State, ct).ConfigureAwait(false);
            }

            totalSw.Stop();
            execution.Status = ExecutionStatus.Completed;
            execution.CurrentJob = null;
            execution.Result = WorkflowResult<TState>.Succeeded(
                execution.State, totalSw.Elapsed, execution.History, execution.BranchOutcomes);

            LogWorkflowCompleted(definition.Name, execution.Id, totalSw.ElapsedMilliseconds, execution.History.Count);

            await EmitEventAsync(events, new WorkflowCompleted<TState>
            {
                WorkflowName = definition.Name,
                ExecutionId = execution.Id,
                Result = execution.Result
            }, ct).ConfigureAwait(false);

            if (_checkpointStore is not null)
                await _checkpointStore.DeleteAsync(execution.Id, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The `when` guard matters (R6): without it, an OperationCanceledException thrown by
            // a job from its own unrelated token — not this run's `ct` — was reported as
            // "Workflow cancelled." with no Exception at all (see WorkflowResult.Cancelled),
            // discarding the real fault. Only our own `ct` being signalled means the caller
            // actually asked to stop; anything else falls through to the catch below.
            totalSw.Stop();
            execution.Status = ExecutionStatus.Cancelled;
            execution.Result = WorkflowResult<TState>.Cancelled(
                execution.State, totalSw.Elapsed, execution.History, execution.BranchOutcomes);
            LogWorkflowCancelled(definition.Name, execution.Id);
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            execution.Status = ExecutionStatus.Faulted;
            execution.Result = WorkflowResult<TState>.Failed(
                execution.State, totalSw.Elapsed, execution.History, ex.Message, ex,
                branchOutcomes: execution.BranchOutcomes);
            LogWorkflowFaulted(ex, definition.Name, execution.Id, ex.Message);

            await EmitEventAsync(events, new WorkflowFaulted<TState>
            {
                WorkflowName = definition.Name,
                ExecutionId = execution.Id,
                Exception = ex,
                State = execution.State
            }).ConfigureAwait(false);
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
            return await descriptor.Job.ExecuteAsync(state, ct).ConfigureAwait(false);

        Func<Task<TState>> pipeline = () => descriptor.Job.ExecuteAsync(state, ct);

        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = () => InvokeMiddlewareAsync(middleware, jobName, state, next, ct);
        }

        return await pipeline().ConfigureAwait(false);
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
                await descriptor.OnFault(state, exception).ConfigureAwait(false);
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
                await definition.OnError(state, jobName, exception).ConfigureAwait(false);
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
            async () => (object)(await next().ConfigureAwait(false))!,
            ct).ConfigureAwait(false);
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
            return await router.RouteAsync(state, ct).ConfigureAwait(false);

        return null;
    }

    // ------------------------------------------------------------------
    //  Fork / Join — parallel branch execution
    // ------------------------------------------------------------------

    private sealed record BranchResult<TState>(
        string FinalJob,
        TState FinalState,
        List<JobExecution> History,
        BranchOutcomeKind Kind,
        Exception? Exception);

    /// <returns>
    /// The join target, or <c>null</c> when a branch hit the budget. Null means the execution is
    /// ending: joining would be meaningless, and the endpoint match would fail anyway because a
    /// stopped branch ends wherever it stopped rather than on a join source.
    /// </returns>
    private async Task<string?> ExecuteForkJoinAsync<TState>(
        WorkflowDefinition<TState> definition,
        ForkConnection fork,
        WorkflowExecution<TState> execution,
        ITrace trace,
        ChannelWriter<WorkflowEvent<TState>>? events,
        BudgetGate budgetGate,
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
                forkSpan, events, budgetGate, branchCt, failFastCts: forkCts)).ToList();

        // Branch faults now come back as results rather than exceptions, so this
        // gathers first and decides after, instead of catching.
        var all = await Task.WhenAll(branchTasks).ConfigureAwait(false);

        // Caller-initiated cancellation is not a branch outcome — propagate it.
        ct.ThrowIfCancellationRequested();

        var faulted = all.Where(r => r.Kind == BranchOutcomeKind.Faulted).ToList();
        var results = all.Where(r => r.Kind == BranchOutcomeKind.Succeeded).ToArray();
        var stopped = all.Any(r => r.Kind == BranchOutcomeKind.Stopped);

        // Task.WhenAll preserves input order and branchTasks is built from fork.Targets in order,
        // so this zip is sound — do not reorder branchTasks.
        var outcomes = fork.Targets
            .Zip(all, (target, r) => new BranchOutcome
            {
                BranchTarget = target,
                FinalJob = r.FinalJob,
                Kind = r.Kind,
                Exception = r.Exception
            })
            .ToList();

        // FailFast's throw is deferred past the recording below: the exception, and whether it
        // fires at all, is decided here but not thrown yet, so a failed fork under the default
        // mode gets the same outcome/history reporting BestEffort has instead of losing it to an
        // early return. ExceptionDispatchInfo.Capture rather than a bare exception reference so
        // the single-fault case still preserves the original stack trace when it is finally
        // thrown — `throw ex` overwrites StackTrace with the rethrow site.
        ExceptionDispatchInfo? failFastSingleFault = null;
        var failFastMultiFault = false;

        if (fork.Mode == ForkMode.FailFast)
        {
            if (faulted.Count == 1)
                failFastSingleFault = ExceptionDispatchInfo.Capture(faulted[0].Exception!);
            else if (faulted.Count > 1)
                failFastMultiFault = true;
        }
        else if (results.Length == 0)
        {
            // BestEffort with every branch failed — unchanged by B1: still throws immediately,
            // without recording outcomes/history. B1 only extends FailFast's reporting.
            throw new AggregateException(
                "All fork branches faulted.", faulted.Select(f => f.Exception!));
        }
        else if (faulted.Count > 0)
        {
            LogForkPartialSuccess(definition.Name, execution.Id, results.Length, fork.Targets.Count);
        }

        // D3/D4: a branch that did not succeed is reported, not swallowed — on the event stream
        // and on the execution, which carries it through to WorkflowResult.BranchOutcomes.
        foreach (var outcome in outcomes.Where(o => !o.Succeeded))
        {
            execution.RecordBranchOutcome(outcome);
            await EmitEventAsync(events, new BranchFaulted<TState>
            {
                WorkflowName = definition.Name,
                ExecutionId = execution.Id,
                Outcome = outcome
            }, ct).ConfigureAwait(false);
        }

        // Record every branch's history, including branches that did not succeed — their partial
        // history (with the failed job's own entry) is exactly what was being dropped before.
        // Hoisted above the FailFast throw below for the same B1 reason as the outcomes loop.
        foreach (var result in all)
        {
            foreach (var jobExec in result.History)
                execution.RecordJobExecution(jobExec);
        }

        failFastSingleFault?.Throw();
        if (failFastMultiFault)
            throw new AggregateException(
                "Fork branch(es) faulted.", faulted.Select(f => f.Exception!));

        // A budget stop ends the execution, so there is nothing to join. Outcomes and history
        // were recorded above, exactly as they are for a fault, so the caller still sees what
        // each branch did before stopping.
        if (stopped)
        {
            LogForkBudgetStopped(definition.Name, execution.Id);
            return null;
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

        // ContextMerge, not Merge: the callback sees every branch's outcome, not just the
        // surviving states, so it can decide what a partial result means.
        execution.State = join.ContextMerge(new JoinContext<TState>
        {
            States = orderedStates,
            Outcomes = outcomes
        });

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
        ChannelWriter<WorkflowEvent<TState>>? events,
        BudgetGate budgetGate,
        CancellationToken ct,
        CancellationTokenSource? failFastCts)
    {
        // Owned here, not inside ExecuteBranchAsync, so a branch that throws still yields the
        // partial history it accumulated — including the failed job's own entry, which
        // ExecuteBranchAsync records before rethrowing.
        var history = new List<JobExecution>();

        await using var branchSpan = parentSpan.StartSpan($"branch:{startJob}", SpanKind.Job);
        try
        {
            return await ExecuteBranchAsync(
                definition, startJob, branchState, executionId, branchSpan, history, events,
                budgetGate, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            branchSpan.RecordError(ex);

            // Captured before failFastCts.CancelAsync() below — that call cancels our own `ct`
            // unconditionally for *any* fault under FailFast (it is what cancels siblings), so
            // reading IsCancellationRequested after it would always be true regardless of what
            // actually caused this exception.
            var ourTokenAlreadyRequestedCancellation = ct.IsCancellationRequested;

            // FailFast still cancels siblings promptly — only the reporting changes here, not the
            // cancellation.
            if (failFastCts is not null)
                await failFastCts.CancelAsync().ConfigureAwait(false);

            // A job can throw OperationCanceledException from its own internal token — a timeout,
            // say — with no connection to the fork's own cancellation. `ex is OCE` alone conflated
            // that with "cancelled by FailFast": under BestEffort it produced an AggregateException
            // with zero inner exceptions when every branch self-cancelled, and under FailFast it
            // fell through to "No matching Join found for branch endpoints: []" for what was really
            // a job-level fault (R6). ourTokenAlreadyRequestedCancellation is true when *our* token
            // (the fork's linked FailFast token, or the outer ct under BestEffort) was the one that
            // fired — including via a child token linked from it — before this branch had any
            // chance to cancel it itself.
            var kind = ex is OperationCanceledException && ourTokenAlreadyRequestedCancellation
                ? BranchOutcomeKind.Cancelled
                : BranchOutcomeKind.Faulted;

            // FinalState is the state as of the fork: a branch's in-flight state is unrecoverable
            // once a job threw. Carried only so the record is well-formed — a non-succeeded branch
            // never contributes a state to the merge.
            return new BranchResult<TState>(
                FinalJob: history.LastOrDefault(h => h.Success)?.JobName ?? startJob,
                FinalState: branchState,
                History: history,
                Kind: kind,
                Exception: kind == BranchOutcomeKind.Faulted ? ex : null);
        }
    }

    private async Task<BranchResult<TState>> ExecuteBranchAsync<TState>(
        WorkflowDefinition<TState> definition,
        string startJob,
        TState state,
        string executionId,
        ISpan branchSpan,
        List<JobExecution> history,
        ChannelWriter<WorkflowEvent<TState>>? events,
        BudgetGate budgetGate,
        CancellationToken ct)
    {
        var currentJobName = startJob;
        var lastCompletedJob = startJob;

        // Branch-local, deliberately not execution.LoopCounters: that dictionary is not
        // thread-safe and concurrent branches would corrupt each other's counts. A branch's
        // iterations are its own.
        var loopCounters = new Dictionary<string, int>();

        while (currentJobName is not null && currentJobName != Workflow.EndMarker)
        {
            ct.ThrowIfCancellationRequested();

            if (!definition.Jobs.TryGetValue(currentJobName, out var descriptor))
                throw new InvalidOperationException(
                    $"Job '{currentJobName}' is not defined in workflow '{definition.Name}'.");

            // Honouring this would mean pausing one of N concurrent branches and
            // resuming it later, which needs branch-local checkpointing that does not exist. The
            // alternative — treating it as a dropped branch — would silently turn "pause for a
            // human" into "skip the gated work", so fail loudly instead.
            if (descriptor.Interrupt is InterruptMode.Before or InterruptMode.After)
                throw new NotSupportedException(
                    $"Job '{currentJobName}' has an interrupt configured but runs inside a forked " +
                    "branch. Interrupts are not supported inside forks — resuming a branch requires " +
                    "branch-local checkpointing, which does not exist yet. Move the interrupt " +
                    "outside the fork, or remove it.");

            await using var jobSpan = branchSpan.StartSpan(currentJobName, SpanKind.Job);
            jobSpan.SetAttribute("workflow", definition.Name);
            jobSpan.SetAttribute("execution_id", executionId);
            jobSpan.SetAttribute("fork.branch", "true");

            WorkflowTraceContext.Value = new TraceInfo(
                definition.Name, executionId, currentJobName,
                CurrentSpan: jobSpan, StoreCompletions: _storeCompletions);

            if (descriptor.OnEnter is not null)
                await descriptor.OnEnter(state).ConfigureAwait(false);

            var jobSw = Stopwatch.StartNew();

            using var timeoutCts = descriptor.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            timeoutCts?.CancelAfter(descriptor.Timeout!.Value);
            var jobCt = timeoutCts?.Token ?? ct;

            LogBranchJobStarting(currentJobName, definition.Name);

            await EmitEventAsync(events, new JobStarted<TState>
            {
                WorkflowName = definition.Name,
                ExecutionId = executionId,
                JobName = currentJobName,
                Branch = startJob
            }, ct).ConfigureAwait(false);

            try
            {
                state = await ExecuteJobWithMiddlewareAsync(descriptor, currentJobName, state, jobCt).ConfigureAwait(false);
                jobSw.Stop();

                if (descriptor.OnExit is not null)
                    await descriptor.OnExit(state).ConfigureAwait(false);

                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, true));

                LogBranchJobCompleted(currentJobName, jobSw.ElapsedMilliseconds);

                await EmitEventAsync(events, new JobCompleted<TState>
                {
                    WorkflowName = definition.Name,
                    ExecutionId = executionId,
                    JobName = currentJobName,
                    Duration = jobSw.Elapsed,
                    State = state,
                    Branch = startJob
                }, ct).ConfigureAwait(false);

                // Deliberately no StateUpdated here. A branch's state is its own until the
                // join merges it, so emitting one would tell a consumer the workflow state
                // changed when it has not. The main path already emits StateUpdated after
                // JoinCompleted, which is the point the merge becomes the execution's state.
            }
            catch (OperationCanceledException) when (
                timeoutCts is not null && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                jobSw.Stop();
                var timeoutMsg = $"Job '{currentJobName}' timed out after {descriptor.Timeout!.Value.TotalSeconds:F1}s.";
                var timeoutEx = new TimeoutException(timeoutMsg);
                jobSpan.RecordError(timeoutEx);
                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, false, timeoutMsg));
                await InvokeFaultHandlersAsync(descriptor, definition, currentJobName, state, timeoutEx).ConfigureAwait(false);
                throw timeoutEx;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                jobSw.Stop();
                jobSpan.RecordError(ex);
                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, false, ex.Message));
                await InvokeFaultHandlersAsync(descriptor, definition, currentJobName, state, ex).ConfigureAwait(false);
                throw;
            }

            lastCompletedJob = currentJobName;

            // The budget check belongs here, not only on the main path: after D4 a branch can
            // loop, so a cycle inside one would otherwise spend without any check ever running —
            // exactly the runaway a guardrail exists for. Stop this branch and let the fork
            // report it; siblings notice independently at their own next check, so nothing is
            // cancelled and ADR-arch-025 D2 stays intact.
            if ((await budgetGate.EvaluateAsync(ct).ConfigureAwait(false)).State == BudgetState.Exceeded)
            {
                LogBranchBudgetStopped(currentJobName, definition.Name);
                return new BranchResult<TState>(
                    lastCompletedJob, state, history, BranchOutcomeKind.Stopped, Exception: null);
            }

            // A nested fork inside a branch is not supported: joining it would need this frame
            // to own an execution and decide how inner branch outcomes roll up into the outer
            // ones. Fail loudly rather than resolve to null and truncate the branch silently.
            if (definition.ResolveFork(currentJobName) is not null)
                throw new NotSupportedException(
                    $"Job '{currentJobName}' starts a fork inside a forked branch. Nested forks are " +
                    "not supported — flatten the fork, or move the inner fork into a SubFlow, which " +
                    "runs its own workflow and supports forks normally.");

            // Loops must be resolved here as well as on the main path. ResolveNextJobAsync sees
            // only direct and router edges, so without this a LoopConnection resolves to null and
            // the branch stops after one pass — surfacing later as a confusing "No matching Join"
            // because the branch ends on the loop job instead of the loop's exit target.
            var loopConn = definition.ResolveLoop(currentJobName);
            if (loopConn is not null)
            {
                loopCounters.TryGetValue(currentJobName, out var iteration);
                iteration++;
                loopCounters[currentJobName] = iteration;

                if (loopConn.Until(state) || iteration >= loopConn.MaxIterations)
                {
                    loopCounters.Remove(currentJobName);
                    currentJobName = loopConn.ExitTarget;
                }
                else
                {
                    jobSpan.SetAttribute("loop.iteration", iteration.ToString());
                    currentJobName = loopConn.LoopTarget;
                }

                continue;
            }

            currentJobName = await ResolveNextJobAsync(definition, currentJobName, state, ct).ConfigureAwait(false);
        }

        return new BranchResult<TState>(
            lastCompletedJob, state, history, BranchOutcomeKind.Succeeded, Exception: null);
    }

    private static async ValueTask EmitEventAsync<TState>(
        ChannelWriter<WorkflowEvent<TState>>? events,
        WorkflowEvent<TState> evt,
        CancellationToken ct = default)
    {
        if (events is null) return;
        try { await events.WriteAsync(evt, ct).ConfigureAwait(false); }
        catch (ChannelClosedException) { }
        catch (OperationCanceledException) { }
    }

    // -- Source-generated structured log methods ----------------------

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Workflow {WorkflowName} [{ExecutionId}] passed its budget warning threshold: estimated {EstimatedCost} > {WarnAtCost}")]
    private partial void LogBudgetWarning(string workflowName, string executionId, decimal estimatedCost, decimal warnAtCost);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Branch job {JobName} in {WorkflowName} stopped: cost budget reached")]
    private partial void LogBranchBudgetStopped(string jobName, string workflowName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Fork in {WorkflowName} [{ExecutionId}] stopped: a branch reached the cost budget")]
    private partial void LogForkBudgetStopped(string workflowName, string executionId);

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
