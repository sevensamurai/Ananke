using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Ananke.Abstractions;
using Ananke.Abstractions.Distributed;
using Ananke.Abstractions.Extensions;
using Ananke.StateMachine.Builder;
using Ananke.StateMachine.Middleware;
using Ananke.StateMachine.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.StateMachine;

/// <summary>
/// Abstract state machine with distributed coordination support. One instance serves any number
/// of contexts, and any number of instances (one per node) can share a store and a lock: state,
/// step, interrupt stack and operational status are kept per context in the store, under
/// <see cref="GetContextKey"/>, and each transition, fault and reset of a context runs under that
/// context's lock.
/// </summary>
/// <typeparam name="C">Context type implementing IBaseContext</typeparam>
/// <typeparam name="S">State enum type</typeparam>
/// <typeparam name="T">Transition enum type</typeparam>
/// <typeparam name="N">Notification enum type</typeparam>
/// <param name="initialState">The state of a context that has no stored record</param>
/// <param name="locker">Distributed lock implementation</param>
/// <param name="store">Key-value data adapter for persisting state</param>
/// <param name="options">Optional configuration options</param>
/// <param name="logger">Optional logger</param>
public abstract class AbstractStateMachine<C, S, T, N>(
    S initialState,
    IDistributedLock locker,
    IKeyValueDataAdapter store,
    StateMachineOptions? options = null,
    ILogger<AbstractStateMachine<C, S, T, N>>? logger = null)
    : IActionStateMachine<C, S, T, N>
    where C : IBaseContext
    where S : Enum
    where T : Enum
    where N : Enum
{
    private readonly S _initialState = initialState;
    private readonly IDistributedLock _locker = locker;
    private readonly IKeyValueDataAdapter _store = store;
    private readonly ILogger<AbstractStateMachine<C, S, T, N>> Log = logger ?? NullLogger<AbstractStateMachine<C, S, T, N>>.Instance;
    private readonly List<ITransitionMiddleware<C, S, T>> _middlewares = [];
    private readonly StateMachineOptions _options = options ?? new StateMachineOptions();

    private Lazy<TransitionBuilder<S, T>>? _table;

    /// <summary>
    /// The transition table, built from <see cref="Transitions"/> once, on first use, by one thread.
    /// </summary>
    private TransitionBuilder<S, T> Builder =>
        LazyInitializer.EnsureInitialized(ref _table, () => new Lazy<TransitionBuilder<S, T>>(BuildTable)).Value;

    private TransitionBuilder<S, T> BuildTable()
    {
        var builder = new TransitionBuilder<S, T>();
        Transitions(builder);
        builder.Build();
        return builder;
    }

    /// <summary>
    /// Declarative transition configuration.
    /// Implementations must define all valid state transitions.
    /// </summary>
    /// <example>
    /// <code>
    /// protected override Action&lt;ITransitionBuilder&lt;State, Transition&gt;&gt; Transitions => builder => builder
    ///     .From(State.Parked).On(Transition.Start).To(State.Moving)
    ///     .From(State.Moving).On(Transition.Stop).To(State.Parked);
    /// </code>
    /// </example>
    protected abstract Action<ITransitionBuilder<S, T>> Transitions { get; }

    /// <summary>
    /// Access to the distributed lock for configuration
    /// </summary>
    public IDistributedLock Locker => _locker;

    /// <summary>
    /// The state of a context that has no stored record
    /// </summary>
    public S InitialState => _initialState;

    /// <summary>
    /// State machine options
    /// </summary>
    public StateMachineOptions Options => _options;

    private string? _typeKeyName;

    /// <summary>
    /// The machine's name in its keys: <see cref="StateMachineOptions.MachineName"/>, or, when that
    /// is <c>null</c>, the machine's type name with its type arguments (<c>OrderMachine&lt;Retail&gt;</c>).
    /// </summary>
    public string MachineName => _options.MachineName ?? (_typeKeyName ??= TypeKeyName(GetType()));

    private static string TypeKeyName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;
        var tick = type.Name.IndexOf('`');
        var name = tick < 0 ? type.Name : type.Name[..tick];
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(TypeKeyName))}>";
    }

    /// <summary>
    /// The store key and lock resource of a context: <c>{KeyPrefix}:{MachineName}:{contextId}</c>,
    /// leaving out an empty prefix or machine name.
    /// </summary>
    /// <param name="contextId">The context's <see cref="IBaseContext.Id"/></param>
    public string GetContextKey(string contextId)
    {
        ArgumentNullException.ThrowIfNull(contextId);
        var machineName = MachineName;
        return (string.IsNullOrEmpty(_options.KeyPrefix), string.IsNullOrEmpty(machineName)) switch
        {
            (false, false) => $"{_options.KeyPrefix}:{machineName}:{contextId}",
            (false, true) => $"{_options.KeyPrefix}:{contextId}",
            (true, false) => $"{machineName}:{contextId}",
            (true, true) => contextId,
        };
    }

    #region Internal Types

    /// <summary>
    /// The record stored for each context.
    /// </summary>
    public class PersistedContext<X>
    {
        /// <summary>
        /// The format version this release of Ananke writes and reads. A record with a higher
        /// <see cref="Version"/> is refused rather than read.
        /// </summary>
        public const int FormatVersion = 1;

        /// <summary>
        /// The record's format version. <c>0</c> for a record written before versions were stored;
        /// set to <see cref="FormatVersion"/> on every write.
        /// </summary>
        public int Version { get; set; }

        public required X State { get; set; }
        public required int Step { get; set; }
        public OperationalStatus OperationalStatus { get; set; } = OperationalStatus.Operative;
        public string? OperationalStatusReason { get; set; }
        public List<X> InterruptStack { get; set; } = [];

        /// <summary>
        /// Optional payload associated with the most recent interrupt transition.
        /// Cleared on resume.
        /// </summary>
        public object? InterruptPayload { get; set; }
    }

    #endregion

    #region Middleware

    /// <summary>
    /// Adds a middleware to the transition pipeline
    /// </summary>
    public void UseMiddleware(ITransitionMiddleware<C, S, T> middleware)
    {
        _middlewares.Add(middleware);
    }

    /// <summary>
    /// Adds a middleware to the transition pipeline
    /// </summary>
    public void UseMiddleware<TMiddleware>() where TMiddleware : ITransitionMiddleware<C, S, T>, new()
    {
        _middlewares.Add(new TMiddleware());
    }

    #endregion

    #region Status

    /// <summary>
    /// Reads a context's stored state and operational status. A context with no record is at
    /// <see cref="InitialState"/> and <see cref="OperationalStatus.Operative"/>. Throws when the
    /// store cannot be read.
    /// </summary>
    /// <param name="context">The context to read</param>
    /// <param name="ct">Cancels the store read</param>
    public async Task<ContextStatus<S>> GetStatusAsync(C context, CancellationToken ct = default)
    {
        var persisted = await ReadContextAsync(GetContextKey(context.Id), ct).ConfigureAwait(false);
        return new ContextStatus<S>(
            persisted.State,
            persisted.Step,
            persisted.OperationalStatus,
            persisted.OperationalStatusReason,
            persisted.InterruptStack.Count > 0);
    }

    /// <summary>
    /// Marks one context as Faulted, on every instance that shares the store: its transitions are
    /// refused until <see cref="ResetAsync"/>. Other contexts are not affected.
    /// </summary>
    /// <param name="context">The context to fault</param>
    /// <param name="reason">Reason for the fault</param>
    /// <param name="ct">Cancels waiting for the lock and the store calls.</param>
    /// <returns>Status change result</returns>
    public virtual async Task<OperationalStatusChange> FaultAsync(C context, string reason, CancellationToken ct = default)
    {
        var change = await ChangeStatusAsync(context, OperationalStatus.Faulted, reason, ct).ConfigureAwait(false);

        if (!change.Success)
        {
            Log.LogDebug("Already Faulted [{Id}]: {Reason}", context.Id, change.Reason);
            return change;
        }

        Log.LogWarning("State machine FAULTED [{Id}]: {Reason}", context.Id, reason);

        Activity.Current?.AddEvent(new ActivityEvent("Ananke.faulted", tags: new ActivityTagsCollection
        {
            { "Ananke.context_id", context.Id },
            { "Ananke.reason", reason },
        }));

        return change;
    }

    /// <summary>
    /// Resets one context from Faulted to Operative.
    /// </summary>
    /// <param name="context">The context to reset</param>
    /// <param name="reason">Reason for the reset (e.g., "Device replaced")</param>
    /// <param name="ct">Cancels waiting for the lock and the store calls.</param>
    /// <returns>Status change result</returns>
    public virtual async Task<OperationalStatusChange> ResetAsync(C context, string reason, CancellationToken ct = default)
    {
        var change = await ChangeStatusAsync(context, OperationalStatus.Operative, reason, ct).ConfigureAwait(false);

        if (!change.Success)
        {
            Log.LogDebug("Already Operative [{Id}]", context.Id);
            return change;
        }

        Log.LogInformation("State machine RESET [{Id}]: {Reason}", context.Id, reason);

        Activity.Current?.AddEvent(new ActivityEvent("Ananke.reset", tags: new ActivityTagsCollection
        {
            { "Ananke.context_id", context.Id },
            { "Ananke.reason", reason },
        }));

        return change;
    }

    /// <summary>
    /// Reads the context and writes <paramref name="status"/> to it under the context's lock, so a
    /// transition running at the same time cannot write over the status. Throws when the lock
    /// cannot be acquired or the store cannot be read or written; nothing is written then.
    /// </summary>
    private async Task<OperationalStatusChange> ChangeStatusAsync(
        C context, OperationalStatus status, string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = GetContextKey(context.Id);

        // The action never throws: an exception is carried out in the result and rethrown here,
        // so the lock neither retries the action nor reports the exception as its own failure.
        var locked = await _locker.RunCoordinatedActionWithRetryAsync<(OperationalStatusChange Change, ExceptionDispatchInfo? Error)>(
            key,
            async () =>
            {
                try
                {
                    var persisted = await ReadContextAsync(key, ct).ConfigureAwait(false);
                    var previous = persisted.OperationalStatus;
                    if (previous == status)
                    {
                        var unchangedReason = status == OperationalStatus.Faulted
                            ? persisted.OperationalStatusReason ?? reason
                            : "Already operative";
                        return (new OperationalStatusChange(false, previous, previous, unchangedReason), null);
                    }

                    persisted.OperationalStatus = status;
                    persisted.OperationalStatusReason = status == OperationalStatus.Faulted ? reason : null;
                    await WriteContextAsync(key, persisted, ct).ConfigureAwait(false);
                    return (new OperationalStatusChange(true, previous, status, reason), null);
                }
                catch (Exception ex)
                {
                    return (default, ExceptionDispatchInfo.Capture(ex));
                }
            },
            _options.LockRetryCount,
            _options.LockRetryDelayMs,
            ct).ConfigureAwait(false);

        if (!locked.LockAcquired)
            throw new TimeoutException($"Could not acquire the lock for context '{context.Id}' ({key}).");
        if (!locked.Success)
        {
            if (locked.Exception is not null)
                ExceptionDispatchInfo.Capture(locked.Exception).Throw();
            throw new InvalidOperationException(locked.ErrorMessage);
        }

        locked.Result.Error?.Throw();
        return locked.Result.Change;
    }

    #endregion

    #region Store

    /// <summary>
    /// Reads a context by its id, as <see cref="GetStatusAsync"/> does. Only a missing key starts a
    /// new context at the initial state; a store that cannot be read throws.
    /// </summary>
    internal Task<PersistedContext<S>> GetPersistedContextAsync(string contextId, CancellationToken ct = default)
        => ReadContextAsync(GetContextKey(contextId), ct);

    /// <summary>
    /// Reads the record under <paramref name="key"/>. Only a missing key starts a new context at the
    /// initial state; a store that cannot be read throws, and so does a record of a newer format.
    /// </summary>
    private async Task<PersistedContext<S>> ReadContextAsync(string key, CancellationToken ct)
    {
        var persisted = await _store.GetValueAsync<PersistedContext<S>>(key, ct).ConfigureAwait(false);
        if (persisted is null)
            return new PersistedContext<S> { State = _initialState, Step = 0 };

        if (persisted.Version > PersistedContext<S>.FormatVersion)
            throw new InvalidOperationException(
                $"The record '{key}' has format version {persisted.Version}; this version of Ananke reads up to " +
                $"{PersistedContext<S>.FormatVersion}.");

        return persisted;
    }

    private Task WriteContextAsync(string key, PersistedContext<S> persisted, CancellationToken ct)
    {
        persisted.Version = PersistedContext<S>.FormatVersion;
        return _store.SetValueAsync(key, persisted, ct);
    }

    #endregion

    #region Core Transition Logic

    /// <summary>
    /// Executes a transition with distributed locking and middleware pipeline.
    /// Refused when the context is Faulted.
    /// </summary>
    protected Task<TransitionResult<S>> InternalTransitionAsync(C context, T transition, CancellationToken ct = default)
        => InternalTransitionAsync(context, transition, payload: null, ct);

    /// <summary>
    /// Executes a transition under the context's lock: reads the context, refuses it when it is
    /// Faulted, runs the middleware pipeline, then evaluates the transition. Carries an optional
    /// payload for interrupt transitions. When the payload implements <see cref="ITimestamped"/>
    /// its <see cref="ITimestamped.EventTime"/> is used as the transition timestamp; otherwise
    /// the time from <see cref="StateMachineOptions.Clock"/> is used.
    /// </summary>
    /// <remarks>
    /// Guards and the exit action run before the new state is stored; the enter action and the
    /// after-transition action run after it. <paramref name="ct"/> stops the transition up to the
    /// exit action, with <see cref="OperationCanceledException"/>; once the exit action has run,
    /// the state is stored whether or not it is cancelled.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the exit action ran.</exception>
    protected async Task<TransitionResult<S>> InternalTransitionAsync(
        C context, T transition, object? payload, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var eventTime = payload is ITimestamped ts ? ts.EventTime : _options.Clock.GetUtcNow();
        var key = GetContextKey(context.Id);

        Log.LogDebug("Transition request: [{Id}] {Transition}", context.Id, transition);

        using var activity = StateMachineActivitySource.Source.StartActivity("transition");
        activity?.SetTag("Ananke.context_id", context.Id);
        activity?.SetTag("Ananke.transition", transition.ToString());

        var result = await ExecuteLockedTransitionAsync(context, key, transition, payload, eventTime, ct)
            .ConfigureAwait(false);

        activity?.SetTag("Ananke.from_state", result.PreviousState.ToString());
        activity?.SetTag("Ananke.to_state", result.CurrentState.ToString());
        activity?.SetTag("Ananke.success", result.Success);

        if (!result.Success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
            if (result.Exception is not null)
            {
                activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    { "exception.type", result.Exception.GetType().FullName },
                    { "exception.message", result.Exception.Message },
                }));
            }
        }

        return result;
    }

    private async Task<TransitionResult<S>> ExecuteLockedTransitionAsync(
        C context, string key, T transition, object? payload, DateTimeOffset eventTime, CancellationToken ct)
    {
        var locked = await _locker.RunCoordinatedActionWithRetryAsync(
            key,
            () => TryExecuteTransitionAsync(context, key, transition, payload, eventTime, ct),
            _options.LockRetryCount,
            _options.LockRetryDelayMs,
            ct).ConfigureAwait(false);

        if (!locked.LockAcquired)
        {
            ct.ThrowIfCancellationRequested();
            Log.LogError(locked.Exception, "Failed to acquire lock for context {Id}: {Error}", context.Id, locked.ErrorMessage);
            return TransitionResult<S>.LockFailed(_initialState) with { EventTimestamp = eventTime };
        }

        if (locked.Result is not null)
            return locked.Result;

        // The transition only throws when cancelled; the lock returns the action's exception in its
        // result unless the token is cancelled, so a cancellation can come back either way.
        if (ct.IsCancellationRequested)
        {
            if (locked.Exception is OperationCanceledException cancelled)
                ExceptionDispatchInfo.Capture(cancelled).Throw();
            ct.ThrowIfCancellationRequested();
        }

        return TransitionResult<S>.Failed(_initialState, locked.ErrorMessage ?? "Unknown error during transition", locked.Exception)
            with
        { EventTimestamp = eventTime };
    }

    /// <summary>
    /// Runs inside the context's lock. Never throws, except <see cref="OperationCanceledException"/>
    /// for <paramref name="ct"/>.
    /// </summary>
    private async Task<TransitionResult<S>> TryExecuteTransitionAsync(
        C context, string key, T transition, object? payload, DateTimeOffset eventTime, CancellationToken ct)
    {
        PersistedContext<S> persisted;
        try
        {
            persisted = await ReadContextAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            Log.LogError(500, ex, "Transition error reading context {Id}: {Message}", context.Id, ex.Message);
            return TransitionResult<S>.Failed(_initialState, ex.Message, ex) with { EventTimestamp = eventTime };
        }

        var storedState = persisted.State;
        if (Log.IsEnabled(LogLevel.Debug))
            Log.LogDebug("CURRENT: {Context}", persisted.ToJson());

        if (persisted.OperationalStatus == OperationalStatus.Faulted)
        {
            Log.LogWarning("Transition BLOCKED [{Id}] - Faulted: {Reason}", context.Id, persisted.OperationalStatusReason);
            return TransitionResult<S>.Failed(storedState, $"Faulted: {persisted.OperationalStatusReason}")
                with
            { EventTimestamp = eventTime };
        }

        // ApplyTransitionAsync changes the record it is given. A middleware that calls next again
        // gets the record as stored now, not the one the earlier call changed.
        PersistedContext<S>? unread = persisted;
        Func<CancellationToken, Task<TransitionResult<S>>> pipeline = async token =>
        {
            var current = Interlocked.Exchange(ref unread, null) ?? await ReadContextAsync(key, token).ConfigureAwait(false);
            return await ApplyTransitionAsync(context, key, current, transition, payload, eventTime, token).ConfigureAwait(false);
        };

        // Wrap with middlewares (in reverse order so first registered runs first)
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = token => middleware.InvokeAsync(context, transition, storedState, next, token);
        }

        try
        {
            return await pipeline(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            Log.LogError(500, ex, "Transition error: {Message}", ex.Message);
            return TransitionResult<S>.Failed(storedState, ex.Message, ex) with { EventTimestamp = eventTime };
        }
    }

    /// <summary>
    /// Evaluates the transition from the stored context and stores the result. Throws when the
    /// state was not stored (a guard, the exit action or the write threw). Once the state is stored,
    /// a throw from the enter or after-transition action returns a failed result with
    /// <see cref="TransitionResult{S}.StateStored"/> set and the stored state as its current state.
    /// </summary>
    private async Task<TransitionResult<S>> ApplyTransitionAsync(
        C context, string key, PersistedContext<S> persisted, T transition, object? payload, DateTimeOffset eventTime,
        CancellationToken ct)
    {
        var previousState = persisted.State;
        var lookup = TransitionBuilder<S, T>.GetKey(previousState, transition);
        var builder = Builder;

        // Check if transition is explicitly defined
        if (!builder.Transitions.TryGetValue(lookup, out var config))
        {
            if (_options.AllowImplicitSelfTransitions)
            {
                // Self-transition: state stays the same, just increment step
                persisted.Step += 1;
                await WriteContextAsync(key, persisted, ct).ConfigureAwait(false);

                Log.LogDebug("SELF-TRANSITION: {State} (implicit)", previousState);
                return TransitionResult<S>.Succeeded(previousState, previousState) with
                {
                    StateStored = true,
                    EventTimestamp = eventTime
                };
            }

            Log.LogWarning("Invalid transition: {State} --({Transition})--> ?", previousState, transition);
            return TransitionResult<S>.InvalidTransition(previousState, transition?.ToString() ?? "unknown")
                with
            { EventTimestamp = eventTime };
        }

        // Check guard condition
        if (config.GuardCondition is not null)
        {
            var guardResult = await config.GuardCondition(context, ct).ConfigureAwait(false);
            if (!guardResult)
            {
                Log.LogDebug("Guard condition failed for transition: {Transition}", transition);
                return TransitionResult<S>.GuardFailed(previousState) with { EventTimestamp = eventTime };
            }
        }

        // ── Interrupt / Resume stack management ──────────────────────
        S resolvedFinalState = config.FinalState;

        if (config.IsInterrupt)
        {
            if (persisted.InterruptStack.Count >= _options.MaxInterruptDepth)
            {
                Log.LogWarning("Max interrupt depth ({Depth}) exceeded for context {Key}",
                    _options.MaxInterruptDepth, key);
                return TransitionResult<S>.Failed(previousState,
                    $"Maximum interrupt depth ({_options.MaxInterruptDepth}) exceeded") with
                { EventTimestamp = eventTime };
            }

            persisted.InterruptStack.Add(previousState);
            persisted.InterruptPayload = payload;
        }
        else if (config.IsResume)
        {
            if (persisted.InterruptStack.Count == 0)
            {
                Log.LogWarning("Resume attempted with empty interrupt stack for context {Key}", key);
                return TransitionResult<S>.Failed(previousState,
                    "Cannot resume: interrupt stack is empty") with
                { EventTimestamp = eventTime };
            }

            resolvedFinalState = persisted.InterruptStack[^1];
            persisted.InterruptStack.RemoveAt(persisted.InterruptStack.Count - 1);
            persisted.InterruptPayload = null;
        }

        // The last point at which cancelling leaves nothing done.
        ct.ThrowIfCancellationRequested();

        // Execute state exit action. Once it has run, the write is not cancelled: stopping there
        // would leave the action's effect done and the state old.
        var writeToken = ct;
        var stateConfigs = builder.StateConfigs;
        if (stateConfigs.TryGetValue(previousState, out var exitStateConfig) && exitStateConfig.OnExitAction is not null)
        {
            writeToken = CancellationToken.None;
            await exitStateConfig.OnExitAction(context, ct).ConfigureAwait(false);
        }

        // Perform transition
        persisted.Step += 1;
        persisted.State = resolvedFinalState;
        await WriteContextAsync(key, persisted, writeToken).ConfigureAwait(false);
        var storedState = resolvedFinalState;
        Log.LogDebug("OK: {PreviousState} --({Transition})--> {NewState}", previousState, transition, resolvedFinalState);

        try
        {
            // Execute state entry action
            if (stateConfigs.TryGetValue(resolvedFinalState, out var entryStateConfig) && entryStateConfig.OnEnterAction is not null)
            {
                await entryStateConfig.OnEnterAction(context, ct).ConfigureAwait(false);
            }

            // Execute after-transition action; a state it returns is stored in place of the target
            if (config.AfterTransitionAction is not null)
            {
                var actionResult = await config.AfterTransitionAction(context, ct).ConfigureAwait(false);
                Log.LogDebug("After-action returned state: {State}", actionResult);

                if (!EqualityComparer<S>.Default.Equals(actionResult, resolvedFinalState))
                {
                    persisted.State = actionResult;
                    await WriteContextAsync(key, persisted, CancellationToken.None).ConfigureAwait(false);
                    storedState = actionResult;
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogError(500, ex, "Action failed after {Key} moved to {State}: {Message}", key, storedState, ex.Message);
            return new TransitionResult<S>
            {
                Success = false,
                StateStored = true,
                PreviousState = previousState,
                CurrentState = storedState,
                ErrorMessage = $"The state moved to {storedState}, then an action failed: {ex.Message}",
                Exception = ex,
                WasInterrupt = config.IsInterrupt,
                WasResume = config.IsResume,
                ResumedFromState = config.IsResume ? previousState : default,
                InterruptPayload = config.IsInterrupt ? payload : null,
                EventTimestamp = eventTime
            };
        }

        return new TransitionResult<S>
        {
            Success = true,
            StateStored = true,
            PreviousState = previousState,
            CurrentState = storedState,
            WasInterrupt = config.IsInterrupt,
            WasResume = config.IsResume,
            ResumedFromState = config.IsResume ? previousState : default,
            InterruptPayload = config.IsInterrupt ? payload : null,
            EventTimestamp = eventTime
        };
    }

    private static bool IsCancellation(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Performs a state transition. Override to add custom behavior like publishing events.
    /// </summary>
    /// <param name="context">The context to transition</param>
    /// <param name="transition">The transition to execute</param>
    /// <param name="ct">Stops the transition up to its exit action</param>
    public abstract Task<TransitionResult<S>> TransitionAsync(C context, T transition, CancellationToken ct = default);

    /// <summary>
    /// Performs a state transition with an optional payload.
    /// Override to add custom behavior. Default delegates to
    /// <see cref="InternalTransitionAsync(C, T, object?, CancellationToken)"/>.
    /// When the payload implements <see cref="ITimestamped"/> its event time is used
    /// as the attributed timestamp on the result; otherwise the time from
    /// <see cref="StateMachineOptions.Clock"/> is used.
    /// </summary>
    /// <param name="context">The context to transition</param>
    /// <param name="transition">The transition to execute</param>
    /// <param name="payload">Arbitrary data to associate with the transition</param>
    /// <param name="ct">Stops the transition up to its exit action</param>
    public virtual Task<TransitionResult<S>> TransitionAsync(C context, T transition, object? payload, CancellationToken ct = default)
        => InternalTransitionAsync(context, transition, payload, ct);

    /// <summary>
    /// Sends a notification without changing state. Override to add custom behavior.
    /// </summary>
    public abstract Task NotifyAsync(C context, N notification, CancellationToken ct = default);

    #endregion
}
