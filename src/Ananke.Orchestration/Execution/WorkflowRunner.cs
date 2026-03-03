using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Middleware;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Execution;

public sealed class WorkflowRunner : IWorkflowRunner
{
    private readonly ICheckpointStore? _checkpointStore;
    private readonly IReadOnlyList<IJobMiddleware<object>> _middlewares;
    private readonly IWorkflowTracer _tracer;
    private readonly bool _storeCompletions;
    private readonly ILogger<WorkflowRunner> _logger;
    private readonly TimeSpan _checkpointTtl;

    public WorkflowRunner(
        ICheckpointStore? checkpointStore = null,
        IEnumerable<IJobMiddleware<object>>? middlewares = null,
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

        var nextJob = await ResolveNextJobAsync(definition, checkpoint.CurrentJob, execution.State, ct);
        return await ExecuteAsync(definition, execution, nextJob, ct);
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

        var nextJob = await ResolveNextJobAsync(definition, checkpoint.CurrentJob, execution.State, ct);
        return await ExecuteAsync(definition, execution, nextJob, ct);
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
            catch { /* Errors communicated via WorkflowFaulted events */ }
        }
    }

    private async Task<WorkflowExecution<TState>> ExecuteAsync<TState>(
        WorkflowDefinition<TState> definition,
        WorkflowExecution<TState> execution,
        string? startJob,
        CancellationToken ct,
        bool skipFirstInterrupt = false,
        ChannelWriter<WorkflowEvent<TState>>? events = null)
    {
        var totalSw = Stopwatch.StartNew();
        execution.Status = ExecutionStatus.Running;
        var currentJobName = startJob;

        _logger.LogInformation(
            "Workflow {WorkflowName} [{ExecutionId}] starting",
            definition.Name, execution.Id);

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

                    _logger.LogInformation(
                        "Workflow {WorkflowName} [{ExecutionId}] interrupted before job {JobName}",
                        definition.Name, execution.Id, currentJobName);

                    await EmitEventAsync(events, new Interrupted<TState>
                    {
                        WorkflowName = definition.Name, ExecutionId = execution.Id,
                        JobName = currentJobName, State = execution.State
                    }, ct);

                    return execution;
                }
                skipFirstInterrupt = false;

                await using var jobSpan = trace.StartSpan(currentJobName, SpanKind.Job);
                jobSpan.SetAttribute("workflow", definition.Name);
                jobSpan.SetAttribute("execution_id", execution.Id);

                WorkflowTraceContext.Value = new TraceInfo(
                    definition.Name, execution.Id, currentJobName, trace, jobSpan,
                    _storeCompletions);

                if (descriptor.OnEnter is not null)
                    await descriptor.OnEnter(execution.State);

                var jobSw = Stopwatch.StartNew();

                using var timeoutCts = descriptor.Timeout.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                    : null;
                timeoutCts?.CancelAfter(descriptor.Timeout!.Value);
                var jobCt = timeoutCts?.Token ?? ct;

                _logger.LogInformation(
                    "Job {JobName} starting in workflow {WorkflowName} [{ExecutionId}]",
                    currentJobName, definition.Name, execution.Id);

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

                    _logger.LogInformation(
                        "Job {JobName} completed in {DurationMs}ms",
                        currentJobName, jobSw.ElapsedMilliseconds);

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

                    _logger.LogInformation(
                        "Workflow {WorkflowName} [{ExecutionId}] interrupted by subflow at job {JobName}",
                        definition.Name, execution.Id, currentJobName);

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
                    _logger.LogError(
                        "Job {JobName} timed out after {TimeoutSeconds:F1}s",
                        currentJobName, descriptor.Timeout!.Value.TotalSeconds);
                    throw timeoutEx;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    jobSw.Stop();
                    jobSpan.RecordError(ex);
                    execution.RecordJobExecution(JobExecution.FromStopwatch(currentJobName, jobSw, false, ex.Message));
                    _logger.LogError(ex, "Job {JobName} failed: {Error}", currentJobName, ex.Message);
                    throw;
                }

                if (_checkpointStore is not null)
                {
                    var checkpoint = Checkpoint<TState>.Create(execution, _checkpointTtl);
                    await _checkpointStore.SaveAsync(checkpoint, ct);
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

                    _logger.LogInformation(
                        "Workflow {WorkflowName} [{ExecutionId}] interrupted after job {JobName}",
                        definition.Name, execution.Id, currentJobName);

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

                currentJobName = await ResolveNextJobAsync(definition, currentJobName, execution.State, ct);
            }

            totalSw.Stop();
            execution.Status = ExecutionStatus.Completed;
            execution.CurrentJob = null;
            execution.Result = WorkflowResult<TState>.Succeeded(execution.State, totalSw.Elapsed, execution.History);

            _logger.LogInformation(
                "Workflow {WorkflowName} [{ExecutionId}] completed in {DurationMs}ms ({JobCount} jobs)",
                definition.Name, execution.Id, totalSw.ElapsedMilliseconds, execution.History.Count);

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
            _logger.LogWarning(
                "Workflow {WorkflowName} [{ExecutionId}] was cancelled",
                definition.Name, execution.Id);
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            execution.Status = ExecutionStatus.Faulted;
            execution.Result = WorkflowResult<TState>.Failed(
                execution.State, totalSw.Elapsed, execution.History, ex.Message, ex);
            _logger.LogError(ex,
                "Workflow {WorkflowName} [{ExecutionId}] faulted: {Error}",
                definition.Name, execution.Id, ex.Message);

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

    private static async Task<TState> InvokeMiddlewareAsync<TState>(
        IJobMiddleware<object> middleware,
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

        _logger.LogInformation(
            "Workflow {WorkflowName} [{ExecutionId}] forking to [{Targets}] ({Mode})",
            definition.Name, execution.Id, string.Join(", ", fork.Targets), fork.Mode);

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

                _logger.LogWarning(
                    "Workflow {WorkflowName} [{ExecutionId}] fork completed with {SucceededCount}/{TotalCount} branches",
                    definition.Name, execution.Id, results.Length, fork.Targets.Count);
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
                $"Defined joins: {string.Join("; ", definition.Joins.Select(j => $"[{string.Join(", ", j.Sources)}] → {j.Target}"))}");

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

        _logger.LogInformation(
            "Workflow {WorkflowName} [{ExecutionId}] joined {BranchCount} branches at {JoinTarget}",
            definition.Name, execution.Id, results.Length, join.Target);

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

            _logger.LogInformation(
                "Branch job {JobName} starting in workflow {WorkflowName}",
                currentJobName, definition.Name);

            try
            {
                state = await ExecuteJobWithMiddlewareAsync(descriptor, currentJobName, state, jobCt);
                jobSw.Stop();

                if (descriptor.OnExit is not null)
                    await descriptor.OnExit(state);

                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, true));

                _logger.LogInformation(
                    "Branch job {JobName} completed in {DurationMs}ms",
                    currentJobName, jobSw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (
                timeoutCts is not null && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                jobSw.Stop();
                var timeoutMsg = $"Job '{currentJobName}' timed out after {descriptor.Timeout!.Value.TotalSeconds:F1}s.";
                jobSpan.RecordError(new TimeoutException(timeoutMsg));
                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, false, timeoutMsg));
                throw new TimeoutException(timeoutMsg);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                jobSw.Stop();
                jobSpan.RecordError(ex);
                history.Add(JobExecution.FromStopwatch(currentJobName, jobSw, false, ex.Message));
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
}
