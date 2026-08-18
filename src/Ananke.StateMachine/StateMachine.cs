using System.Threading.Channels;
using Ananke.Abstractions;
using Ananke.StateMachine.Builder;

namespace Ananke.StateMachine;

/// <summary>
/// Simplified state machine interface — 2 type parameters instead of 4.
/// For in-process use without distributed locking, context types, or notifications.
/// <para>
/// For distributed scenarios with context and notification support, use
/// <see cref="IActionStateMachine{C,S,T,N}"/> and <see cref="AbstractStateMachine{C,S,T,N}"/>.
/// </para>
/// </summary>
/// <typeparam name="S">State enum type.</typeparam>
/// <typeparam name="T">Transition enum type.</typeparam>
public interface IStateMachine<S, T>
    where S : Enum
    where T : Enum
{
    /// <summary>
    /// Fires a transition, optionally carrying a payload for interrupt transitions.
    /// </summary>
    /// <param name="transition">The transition to fire.</param>
    /// <param name="payload">Optional payload, used by interrupt transitions.</param>
    /// <param name="ct">
    /// Abandons the wait for the serialization gate. Transitions are gate-serialized, so a caller
    /// can otherwise be blocked indefinitely behind an in-flight transition with no way out.
    /// </param>
    Task<TransitionResult<S>> FireAsync(T transition, object? payload = null, CancellationToken ct = default);

    /// <summary>The current state of the machine.</summary>
    S CurrentState { get; }

    /// <summary><c>true</c> while the interrupt stack is non-empty.</summary>
    bool IsInterrupted { get; }
}

/// <summary>
/// Factory for creating simplified state machines via configuration instead of subclassing.
/// </summary>
/// <example>
/// <code>
/// var machine = StateMachine.Create&lt;Phase, Action&gt;(Phase.Searching, b =&gt; b
///     .From(Phase.Searching).On(Action.StartPaperwork).To(Phase.Paperwork)
///     .From(Phase.Searching).On(Action.Interrupt).ToInterrupt(Phase.Searching)
///     .From(Phase.Searching).On(Action.Resume).ToResume());
///
/// await machine.FireAsync(Action.StartPaperwork);
/// </code>
/// </example>
public static class StateMachine
{
    /// <summary>
    /// Creates a new state machine configured via the fluent transition builder.
    /// </summary>
    /// <param name="initialState">The starting state of the machine.</param>
    /// <param name="configure">
    /// Configures transitions, guards, and interrupt declarations using
    /// the <see cref="ITransitionBuilder{S,T}"/> fluent API.
    /// </param>
    /// <param name="options">Optional configuration (self-transitions, interrupt depth, etc.).</param>
    public static StateMachine<S, T> Create<S, T>(
        S initialState,
        Action<ITransitionBuilder<S, T>> configure,
        StateMachineOptions? options = null)
        where S : Enum
        where T : Enum
    {
        ArgumentNullException.ThrowIfNull(configure);
        return new StateMachine<S, T>(initialState, configure, options);
    }
}

/// <summary>
/// Simplified state machine that is configured via <see cref="StateMachine.Create{S,T}"/>
/// instead of subclassing. Supports guards, interrupt stack with push/pop/depth,
/// payload delivery, and cancellable state work via <see cref="OnEnter"/>.
/// <para>
/// <b>Thread safety:</b> <see cref="FireAsync"/> is serialized internally.
/// <see cref="OnEnter"/> work runs in the background and is cancelled
/// when the machine leaves the state.
/// </para>
/// </summary>
public sealed class StateMachine<S, T> : IStateMachine<S, T>, IDisposable
    where S : Enum
    where T : Enum
{
    private readonly TransitionBuilder<S, T> _builder;
    private readonly StateMachineOptions _options;
    private readonly List<S> _interruptStack = [];
    private readonly Dictionary<S, Func<CancellationToken, Task>> _stateWork = [];
    private readonly Dictionary<S, Func<Task>> _stateExitWork = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Func<object?, CancellationToken, Task>? _onInterrupt;
    private CancellationTokenSource? _currentStateCts;
    private Task? _currentWork;
    private TaskCompletionSource? _transitionBridge;
    private readonly List<Func<object, S, Task>> _insightHandlers = [];

    // Internal test hook — written by StartStateWork, read by WhenEnteredAsync.
    private readonly Channel<S> _enteredChannel = Channel.CreateUnbounded<S>();

    /// <inheritdoc />
    public S CurrentState { get; private set; }

    /// <inheritdoc />
    public bool IsInterrupted => _interruptStack.Count > 0;

    /// <summary>
    /// The background task for the current state's <see cref="OnEnter"/> work.
    /// <c>null</c> if the current state has no registered work and no transition is in progress.
    /// During a transition this returns a pending bridge task that resolves when the
    /// new state's work starts (or completes immediately if the new state has no work).
    /// Callers can await this to observe completion, errors, or cancellation.
    /// </summary>
    public Task? CurrentWork => _currentWork;

    internal StateMachine(
        S initialState,
        Action<ITransitionBuilder<S, T>> configure,
        StateMachineOptions? options)
    {
        CurrentState = initialState;
        _options = options ?? new StateMachineOptions();
        _builder = new TransitionBuilder<S, T>();
        configure(_builder);
        _builder.Build();
    }

    // ── Phase 2: State work registration ─────────────────────────

    /// <summary>
    /// Registers cancellable work to run when the machine enters the given state.
    /// The <see cref="CancellationToken"/> is cancelled when the machine leaves the state
    /// (normal transition or interrupt). Work runs in the background — <see cref="FireAsync"/>
    /// does not await it. Observe via <see cref="CurrentWork"/>.
    /// </summary>
    public StateMachine<S, T> OnEnter(S state, Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _stateWork[state] = work;
        return this;
    }

    /// <summary>
    /// Registers cleanup to run when the machine exits the given state.
    /// Runs after the state's CTS is cancelled but before entering the new state.
    /// </summary>
    public StateMachine<S, T> OnExit(S state, Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _stateExitWork[state] = work;
        return this;
    }

    /// <summary>
    /// Registers a callback invoked when an interrupt transition succeeds.
    /// The callback receives the payload passed to <see cref="FireAsync"/> and
    /// runs after the old state's work is cancelled, before the new state's work starts.
    /// </summary>
    public StateMachine<S, T> OnInterrupt(Func<object?, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _onInterrupt = handler;
        return this;
    }

    /// <summary>
    /// Registers an <see cref="IInterruptSink{T}"/> to receive interrupt payloads.
    /// When an interrupt transition succeeds, the payload is delivered to the sink.
    /// </summary>
    /// <remarks>
    /// If the payload passed to <see cref="FireAsync"/> is not of type
    /// <typeparamref name="TPayload"/>, an <see cref="InvalidCastException"/> is thrown
    /// so the mismatch is surfaced immediately rather than silently dropped.
    /// To allow null or missing payloads without throwing, pass <c>null</c> explicitly
    /// and guard inside the sink implementation.
    /// </remarks>
    public StateMachine<S, T> OnInterrupt<TPayload>(IInterruptSink<TPayload> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _onInterrupt = async (payload, ct) =>
        {
            if (payload is TPayload typed)
            {
                await sink.InterruptAsync(typed, ct).ConfigureAwait(false);
                return;
            }

            // 5.8: Null payload is permitted (interrupt fired without data).
            if (payload is null)
                return;

            // 5.8: Non-null payload of the wrong type is a programming error — throw so
            // the caller learns about the mismatch instead of silently discarding the signal.
            throw new InvalidCastException(
                $"Interrupt payload is of type '{payload.GetType().FullName}' but the registered " +
                $"sink expects '{typeof(TPayload).FullName}'. Ensure FireAsync is called with the " +
                "correct payload type.");
        };
        return this;
    }

    // ── Insight signal ───────────────────────────────────────────

    /// <summary>
    /// Registers a handler invoked when a background process signals an insight
    /// via <see cref="SignalInsightAsync{TInsight}"/>. The handler receives the
    /// insight and the current state, enabling state-aware routing (e.g. inline
    /// delivery vs. buffering vs. offline notification).
    /// <para>
    /// Handlers run under the transition gate (<see cref="SemaphoreSlim"/>),
    /// serialized with <see cref="FireAsync"/>. They must not block for long.
    /// </para>
    /// </summary>
    public StateMachine<S, T> OnInsight<TInsight>(Func<TInsight, S, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _insightHandlers.Add(async (obj, state) =>
        {
            if (obj is TInsight typed)
                await handler(typed, state).ConfigureAwait(false);
        });
        return this;
    }

    /// <summary>
    /// Delivers an insight from any thread. Gate-serialized with
    /// <see cref="FireAsync"/> — only one of them runs at a time.
    /// Does not cause a state transition.
    /// </summary>
    /// <param name="insight">The insight to deliver.</param>
    /// <param name="ct">Abandons the wait for the serialization gate shared with <see cref="FireAsync"/>.</param>
    public async Task SignalInsightAsync<TInsight>(TInsight insight, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(insight);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var handler in _insightHandlers)
            {
                try
                {
                    await handler(insight, CurrentState).ConfigureAwait(false);
                }
                catch
                {
                    // Handler failure must not block other handlers or poison the gate.
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Core transition ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task<TransitionResult<S>> FireAsync(
        T transition, object? payload = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExecuteTransitionAsync(transition, payload).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TransitionResult<S>> ExecuteTransitionAsync(T transition, object? payload)
    {
        var previousState = CurrentState;

        var key = TransitionBuilder<S, T>.GetKey(CurrentState, transition);

        // Check if transition is explicitly defined
        if (!_builder.Transitions.TryGetValue(key, out var config))
        {
            if (_options.AllowImplicitSelfTransitions)
                return TransitionResult<S>.Succeeded(previousState, CurrentState);

            return TransitionResult<S>.InvalidTransition(
                CurrentState, transition?.ToString() ?? "unknown");
        }

        // Check guard condition
        if (config.GuardCondition is not null)
        {
            if (!await config.GuardCondition().ConfigureAwait(false))
                return TransitionResult<S>.GuardFailed(CurrentState);
        }

        // ── Interrupt / Resume stack management ──────────────────
        var resolvedFinalState = config.FinalState;

        if (config.IsInterrupt)
        {
            if (_interruptStack.Count >= _options.MaxInterruptDepth)
                return TransitionResult<S>.Failed(CurrentState,
                    $"Maximum interrupt depth ({_options.MaxInterruptDepth}) exceeded");

            _interruptStack.Add(CurrentState);
        }
        else if (config.IsResume)
        {
            if (_interruptStack.Count == 0)
                return TransitionResult<S>.Failed(CurrentState,
                    "Cannot resume: interrupt stack is empty");

            resolvedFinalState = _interruptStack[^1];
            _interruptStack.RemoveAt(_interruptStack.Count - 1);
        }

        // ── Leave old state ──────────────────────────────────────

        // Capture the old work before planting the bridge so we can
        // observe its exception (prevents UnobservedTaskException).
        var oldWork = _currentWork;

        // Plant a bridge task so observers never see a null gap
        // between cancelling old work and starting new work.
        var bridge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transitionBridge = bridge;
        _currentWork = bridge.Task;

        // Cancel current state's background work
        CancelCurrentWork(oldWork);

        // Builder-level OnExit (lightweight, synchronous hooks)
        if (_builder.StateConfigs.TryGetValue(previousState, out var exitStateConfig)
            && exitStateConfig.OnExitAction is not null)
            await exitStateConfig.OnExitAction().ConfigureAwait(false);

        // Machine-level OnExit
        if (_stateExitWork.TryGetValue(previousState, out var exitWork))
            await exitWork().ConfigureAwait(false);

        // ── Deliver interrupt payload ────────────────────────────
        if (config.IsInterrupt && _onInterrupt is not null)
            await _onInterrupt(payload, default).ConfigureAwait(false);

        // ── Transition ───────────────────────────────────────────
        CurrentState = resolvedFinalState;

        // After-transition action (may modify final state)
        if (config.AfterTransitionAction is not null)
        {
            var actionResult = await config.AfterTransitionAction().ConfigureAwait(false);
            if (!EqualityComparer<S>.Default.Equals(actionResult, resolvedFinalState))
                CurrentState = actionResult;
        }

        // ── Enter new state ──────────────────────────────────────

        // Builder-level OnEnter (lightweight, synchronous hooks)
        if (_builder.StateConfigs.TryGetValue(CurrentState, out var enterStateConfig)
            && enterStateConfig.OnEnterAction is not null)
            await enterStateConfig.OnEnterAction().ConfigureAwait(false);

        // Machine-level OnEnter (background, cancellable work)
        StartStateWork(CurrentState);

        // If StartStateWork didn't assign real work, clear the bridge
        // so CurrentWork returns null (idle state).
        if (_currentWork == bridge.Task)
            _currentWork = null;

        // Resolve the bridge — observers that awaited the bridge task
        // will now re-read CurrentWork and find the real work (or null).
        _transitionBridge = null;
        bridge.TrySetCanceled();

        return new TransitionResult<S>
        {
            Success = true,
            PreviousState = previousState,
            CurrentState = CurrentState,
            WasInterrupt = config.IsInterrupt,
            WasResume = config.IsResume,
            ResumedFromState = config.IsResume ? previousState : default,
            InterruptPayload = config.IsInterrupt ? payload : null
        };
    }

    // ── CTS lifecycle ────────────────────────────────────────────

    private void CancelCurrentWork(Task? oldWork = null)
    {
        if (_currentStateCts is null)
            return;

        _currentStateCts.Cancel();

        // Observe the old task to prevent unobserved task exceptions.
        oldWork ??= _currentWork;
        if (oldWork is not null && oldWork != _transitionBridge?.Task)
        {
            _ = oldWork.ContinueWith(
                static t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        _currentStateCts.Dispose();
        _currentStateCts = null;
        // Do NOT null _currentWork — the bridge task is already in place.
    }

    private void StartStateWork(S state)
    {
        if (!_stateWork.TryGetValue(state, out var work))
            return;

        // Deliberately NOT derived from any caller's token (Q31, read 2026-08-18). This CTS scopes
        // the state's background work to the *state's* lifetime — it is cancelled on state exit,
        // which is how leaving a state stops whatever that state was doing. A caller's token
        // answers a different question ("abandon my call"), and linking the two would let a
        // completed FireAsync tear down work belonging to the state it just entered.
        var cts = new CancellationTokenSource();
        _currentStateCts = cts;
        // Invoke work synchronously up to its first await (which runs any setup code
        // the caller put before the first yield), then write to the hook channel so
        // WhenEnteredAsync only resolves after that initial synchronous section has run.
        _currentWork = Task.Run(async () =>
        {
            var workTask = work(cts.Token);
            _enteredChannel.Writer.TryWrite(state);
            await workTask.ConfigureAwait(false);
        });
    }

    // ── Internal test hook ────────────────────────────────────────────────
    // Exposed to Ananke.StateMachine.Tests via the project-level InternalsVisibleTo.

    /// <summary>
    /// Returns a <see cref="Task"/> that completes the next time the machine enters
    /// <paramref name="state"/> and background work is started for it.
    /// Items written before this is called are never lost (unbounded channel).
    /// Always pair with <c>WaitAsync(TimeSpan.FromSeconds(5))</c>.
    /// </summary>
    internal async Task WhenEnteredAsync(S state)
    {
        await foreach (var entered in _enteredChannel.Reader.ReadAllAsync().ConfigureAwait(false))
            if (EqualityComparer<S>.Default.Equals(entered, state))
                return;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _enteredChannel.Writer.TryComplete();
        _transitionBridge?.TrySetCanceled();
        _transitionBridge = null;
        CancelCurrentWork();
        _currentWork = null;
        _gate.Dispose();
    }
}
