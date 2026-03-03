using System.Diagnostics;
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
/// Abstract state machine with distributed coordination support.
/// Uses RedLock for distributed computing/coordination.
/// </summary>
/// <typeparam name="C">Context type implementing IBaseContext</typeparam>
/// <typeparam name="S">State enum type</typeparam>
/// <typeparam name="T">Transition enum type</typeparam>
/// <typeparam name="N">Notification enum type</typeparam>
/// <param name="initialState">The initial state of the machine</param>
/// <param name="locker">Distributed lock implementation</param>
/// <param name="options">Optional configuration options</param>
/// <param name="logger">Optional logger</param>
public abstract class AbstractStateMachine<C, S, T, N>(
    S initialState,
    IDistributedLock locker,
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
    private readonly ILogger<AbstractStateMachine<C, S, T, N>> Log = logger ?? NullLogger<AbstractStateMachine<C, S, T, N>>.Instance;
    private readonly List<ITransitionMiddleware<C, S, T>> _middlewares = [];
    private readonly StateMachineOptions _options = options ?? new StateMachineOptions();
    
    private TransitionBuilder<S, T>? _cachedBuilder;

    /// <summary>
    /// Lazily initialized builder that auto-configures on first access
    /// </summary>
    private TransitionBuilder<S, T> Builder
    {
        get
        {
            if (_cachedBuilder is null)
            {
                _cachedBuilder = new TransitionBuilder<S, T>();
                Transitions(_cachedBuilder);
                _cachedBuilder.Build();
            }
            return _cachedBuilder;
        }
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
    /// Current state of the state machine
    /// </summary>
    public S CurrentState { get; internal set; } = initialState;

    /// <summary>
    /// The initial state this machine was constructed with
    /// </summary>
    public S InitialState => _initialState;

    /// <summary>
    /// State machine options
    /// </summary>
    public StateMachineOptions Options => _options;

    /// <summary>
    /// Current operational status (Operative/Faulted)
    /// </summary>
    public OperationalStatus OperationalStatus { get; private set; } = OperationalStatus.Operative;

    /// <summary>
    /// Reason for current operational status (populated when Faulted)
    /// </summary>
    public string? OperationalStatusReason { get; private set; }

    #region Internal Types

    public class PersistedContext<X>
    {
        public required X State { get; set; }
        public required int Step { get; set; }
        public OperationalStatus OperationalStatus { get; set; } = OperationalStatus.Operative;
        public string? OperationalStatusReason { get; set; }
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

    #region Operational Status

    /// <summary>
    /// Marks the state machine as Faulted. All transitions will be blocked until Reset.
    /// </summary>
    /// <param name="context">Context identifying the instance</param>
    /// <param name="reason">Reason for the fault</param>
    /// <returns>Status change result</returns>
    public virtual async Task<OperationalStatusChange> FaultAsync(C context, string reason)
    {
        var previous = OperationalStatus;

        if (previous == OperationalStatus.Faulted)
        {
            Log.LogDebug("Already Faulted [{Id}]: {Reason}", context.Id, OperationalStatusReason);
            return new OperationalStatusChange(false, previous, previous, OperationalStatusReason ?? reason);
        }

        OperationalStatus = OperationalStatus.Faulted;
        OperationalStatusReason = reason;

        Log.LogWarning("State machine FAULTED [{Id}]: {Reason}", context.Id, reason);

        Activity.Current?.AddEvent(new ActivityEvent("Ananke.faulted", tags: new ActivityTagsCollection
        {
            { "Ananke.context_id", context.Id },
            { "Ananke.reason", reason },
        }));

        // Persist faulted status
        await PersistOperationalStatusAsync(context.Id);

        return new OperationalStatusChange(true, previous, OperationalStatus.Faulted, reason);
    }

    /// <summary>
    /// Resets the state machine from Faulted to Operative.
    /// </summary>
    /// <param name="context">Context identifying the instance</param>
    /// <param name="reason">Reason for the reset (e.g., "Device replaced")</param>
    /// <returns>Status change result</returns>
    public virtual async Task<OperationalStatusChange> ResetAsync(C context, string reason)
    {
        var previous = OperationalStatus;
        
        if (previous == OperationalStatus.Operative)
        {
            Log.LogDebug("Already Operative [{Id}]", context.Id);
            return new OperationalStatusChange(false, previous, previous, "Already operative");
        }
        
        OperationalStatus = OperationalStatus.Operative;
        OperationalStatusReason = null;

        Log.LogInformation("State machine RESET [{Id}]: {Reason}", context.Id, reason);

        Activity.Current?.AddEvent(new ActivityEvent("Ananke.reset", tags: new ActivityTagsCollection
        {
            { "Ananke.context_id", context.Id },
            { "Ananke.reason", reason },
        }));

        // Persist operative status
        await PersistOperationalStatusAsync(context.Id);
        
        return new OperationalStatusChange(true, previous, OperationalStatus.Operative, reason);
    }

    /// <summary>
    /// Persists operational status to distributed state
    /// </summary>
    private async Task PersistOperationalStatusAsync(long id)
    {
        // Capture values before GetPersistedContextAsync, which restores
        // OperationalStatus from the (stale) persisted state as a side-effect.
        var statusToSave = OperationalStatus;
        var reasonToSave = OperationalStatusReason;

        var persistedContext = await GetPersistedContextAsync(id);

        // Re-apply the intended values
        OperationalStatus = statusToSave;
        OperationalStatusReason = reasonToSave;
        persistedContext.OperationalStatus = statusToSave;
        persistedContext.OperationalStatusReason = reasonToSave;
        await _locker.SetValueAsync(id.ToString(), persistedContext);
    }

    #endregion

    #region Core Transition Logic

    internal async Task<PersistedContext<S>> GetPersistedContextAsync(long id)
    {
        var context = await _locker.GetValueAsync<PersistedContext<S>>(id.ToString());

        if (context != null)
        {
            // Restore operational status from persisted state
            OperationalStatus = context.OperationalStatus;
            OperationalStatusReason = context.OperationalStatusReason;
        }

        return context ?? new PersistedContext<S> { State = _initialState, Step = 0 };
    }

    internal async Task<TransitionResult<S>> TryExecuteTransitionAsync(long id, T transition)
    {
        try
        {
            var persistedContext = await GetPersistedContextAsync(id);
            var previousState = persistedContext.State;
            CurrentState = previousState;

            Log.LogDebug("CURRENT: {Context}", persistedContext.ToJson());

            var key = TransitionBuilder<S, T>.GetKey(CurrentState, transition);
            var transitions = Builder.Transitions;

            // Check if transition is explicitly defined
            if (!transitions.TryGetValue(key, out var config))
            {
                // Check for implicit self-transition
                if (_options.AllowImplicitSelfTransitions)
                {
                    // Self-transition: state stays the same, just increment step
                    persistedContext.Step += 1;
                    await _locker.SetValueAsync(id.ToString(), persistedContext);
                    
                    Log.LogDebug("SELF-TRANSITION: {State} (implicit)", CurrentState);
                    return TransitionResult<S>.Succeeded(previousState, CurrentState);
                }

                Log.LogWarning("Invalid transition: {State} --({Transition})--> ?", CurrentState, transition);
                return TransitionResult<S>.InvalidTransition(CurrentState, transition?.ToString() ?? "unknown");
            }

            // Check guard condition
            if (config.GuardCondition is not null)
            {
                var guardResult = await config.GuardCondition();
                if (!guardResult)
                {
                    Log.LogDebug("Guard condition failed for transition: {Transition}", transition);
                    return TransitionResult<S>.GuardFailed(CurrentState);
                }
            }

            // Execute state exit action
            var stateConfigs = Builder.StateConfigs;
            if (stateConfigs.TryGetValue(previousState, out var exitStateConfig) && exitStateConfig.OnExitAction is not null)
            {
                await exitStateConfig.OnExitAction();
            }

            // Perform transition
            persistedContext.Step += 1;
            persistedContext.State = config.FinalState;
            CurrentState = config.FinalState;

            await _locker.SetValueAsync(id.ToString(), persistedContext);
            Log.LogDebug("OK: {PreviousState} --({Transition})--> {NewState}", previousState, transition, config.FinalState);

            // Execute state entry action
            if (stateConfigs.TryGetValue(config.FinalState, out var entryStateConfig) && entryStateConfig.OnEnterAction is not null)
            {
                await entryStateConfig.OnEnterAction();
            }

            // Execute after-transition action
            if (config.AfterTransitionAction is not null)
            {
                var actionResult = await config.AfterTransitionAction();
                Log.LogDebug("After-action returned state: {State}", actionResult);
                
                // If action modifies state, update it
                if (!EqualityComparer<S>.Default.Equals(actionResult, config.FinalState))
                {
                    persistedContext.State = actionResult;
                    CurrentState = actionResult;
                    await _locker.SetValueAsync(id.ToString(), persistedContext);
                }
                
                return TransitionResult<S>.Succeeded(previousState, actionResult);
            }

            return TransitionResult<S>.Succeeded(previousState, config.FinalState);
        }
        catch (Exception ex)
        {
            Log.LogError(500, ex, "Transition error: {Message}", ex.Message);
            return TransitionResult<S>.Failed(CurrentState, ex.Message, ex);
        }
    }

    /// <summary>
    /// Executes a transition with distributed locking and middleware pipeline.
    /// Blocked when OperationalStatus is Faulted.
    /// </summary>
    protected async Task<TransitionResult<S>> InternalTransitionAsync(C context, T transition)
    {
        // Gate: Block transitions if Faulted
        if (OperationalStatus == OperationalStatus.Faulted)
        {
            Log.LogWarning("Transition BLOCKED [{Id}] - Faulted: {Reason}", 
                context.Id, OperationalStatusReason);
            return TransitionResult<S>.Failed(CurrentState, $"Faulted: {OperationalStatusReason}");
        }

        Log.LogDebug("Transition request: [{Id}] {Transition}", context.Id, transition);

        using var activity = StateMachineActivitySource.Source.StartActivity("transition");
        activity?.SetTag("Ananke.context_id", context.Id);
        activity?.SetTag("Ananke.transition", transition.ToString());
        activity?.SetTag("Ananke.from_state", CurrentState.ToString());

        // Build the middleware pipeline
        Func<Task<TransitionResult<S>>> pipeline = () => ExecuteLockedTransitionAsync(context.Id, transition);

        // Wrap with middlewares (in reverse order so first registered runs first)
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = () => middleware.InvokeAsync(context, transition, CurrentState, next);
        }

        var result = await pipeline();

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

    private async Task<TransitionResult<S>> ExecuteLockedTransitionAsync(long id, T transition)
    {
        var result = await _locker.RunCoordinatedActionWithRetryAsync(
            id.ToString(),
            () => TryExecuteTransitionAsync(id, transition),
            _options.LockRetryCount,
            _options.LockRetryDelayMs);

        if (!result.LockAcquired)
        {
            Log.LogError("Failed to acquire lock for context {Id}", id);
            return TransitionResult<S>.LockFailed(CurrentState);
        }

        return result.Result ?? TransitionResult<S>.Failed(CurrentState, "Unknown error during transition");
    }

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Performs a state transition. Override to add custom behavior like publishing events.
    /// </summary>
    public abstract Task<TransitionResult<S>> TransitionAsync(C context, T transition);

    /// <summary>
    /// Sends a notification without changing state. Override to add custom behavior.
    /// </summary>
    public abstract Task NotifyAsync(C context, N notification);

    #endregion
}