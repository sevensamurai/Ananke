namespace Ananke.StateMachine.Builder;

/// <summary>
/// Implementation of the fluent transition builder
/// </summary>
public class TransitionBuilder<S, T> :
    IFromStateBuilder<S, T>,
    IToStateBuilder<S, T>,
    ITransitionConfigBuilder<S, T>,
    IStateConfigBuilder<S, T>
    where S : Enum
    where T : Enum
{
    private readonly Dictionary<string, TransitionConfig<S, T>> _transitions = [];
    private readonly Dictionary<S, StateConfig<S>> _stateConfigs = [];

    // Current builder state
    private S[]? _currentFromStates;
    private T? _currentTransition;
    private S? _currentTargetState;
    private S? _currentConfigState;
    private Func<Task<bool>>? _currentGuard;
    private Func<Task<S>>? _currentAction;
    private bool _isInterrupt;
    private bool _isResume;

    /// <summary>
    /// Gets all configured transitions
    /// </summary>
    public IReadOnlyDictionary<string, TransitionConfig<S, T>> Transitions => _transitions;

    /// <summary>
    /// Gets all state configurations
    /// </summary>
    public IReadOnlyDictionary<S, StateConfig<S>> StateConfigs => _stateConfigs;

    public IFromStateBuilder<S, T> From(S state)
    {
        FinalizeCurrentTransition();
        _currentFromStates = [state];
        return this;
    }

    public IFromStateBuilder<S, T> FromAny(params S[] states)
    {
        FinalizeCurrentTransition();
        _currentFromStates = states;
        return this;
    }

    public IToStateBuilder<S, T> On(T transition)
    {
        _currentTransition = transition;
        return this;
    }

    public ITransitionConfigBuilder<S, T> To(S targetState)
    {
        _currentTargetState = targetState;
        return this;
    }

    public ITransitionConfigBuilder<S, T> ToInterrupt(S interruptState)
    {
        _currentTargetState = interruptState;
        _isInterrupt = true;
        return this;
    }

    public ITransitionConfigBuilder<S, T> ToResume()
    {
        _isResume = true;
        return this;
    }

    public ITransitionConfigBuilder<S, T> When(Func<bool> condition)
    {
        _currentGuard = () => Task.FromResult(condition());
        return this;
    }

    public ITransitionConfigBuilder<S, T> WhenAsync(Func<Task<bool>> condition)
    {
        _currentGuard = condition;
        return this;
    }

    public ITransitionConfigBuilder<S, T> WithAction(Func<Task> action)
    {
        var targetState = _currentTargetState!;
        _currentAction = async () =>
        {
            await action();
            return targetState;
        };
        return this;
    }

    public ITransitionConfigBuilder<S, T> WithAction(Func<Task<S>> action)
    {
        _currentAction = action;
        return this;
    }

    public IStateConfigBuilder<S, T> State(S state)
    {
        FinalizeCurrentTransition();
        _currentConfigState = state;

        if (!_stateConfigs.ContainsKey(state))
        {
            _stateConfigs[state] = new StateConfig<S> { State = state };
        }

        return this;
    }

    public IStateConfigBuilder<S, T> OnEnter(Func<Task> action)
    {
        if (_currentConfigState is not null && _stateConfigs.TryGetValue(_currentConfigState, out var config))
        {
            config.OnEnterAction = action;
        }
        return this;
    }

    public IStateConfigBuilder<S, T> OnExit(Func<Task> action)
    {
        if (_currentConfigState is not null && _stateConfigs.TryGetValue(_currentConfigState, out var config))
        {
            config.OnExitAction = action;
        }
        return this;
    }

    /// <summary>
    /// Finalizes the builder and returns all configurations
    /// </summary>
    public void Build()
    {
        FinalizeCurrentTransition();
    }

    private void FinalizeCurrentTransition()
    {
        if (_currentFromStates is null || _currentTransition is null || (_currentTargetState is null && !_isResume))
        {
            // Reset state for next chain
            _currentFromStates = null;
            _currentTransition = default;
            _currentTargetState = default;
            _currentConfigState = default;
            _currentGuard = null;
            _currentAction = null;
            _isInterrupt = false;
            _isResume = false;
            return;
        }

        // For resume transitions, use default(S) as a placeholder — resolved at runtime from the interrupt stack
        var finalState = _currentTargetState ?? default(S)!;

        foreach (var fromState in _currentFromStates)
        {
            var key = GetKey(fromState, _currentTransition);
            if (!_transitions.ContainsKey(key))
            {
                _transitions[key] = new TransitionConfig<S, T>
                {
                    InitialState = fromState,
                    Transition = _currentTransition,
                    FinalState = finalState,
                    GuardCondition = _currentGuard,
                    AfterTransitionAction = _currentAction,
                    IsInterrupt = _isInterrupt,
                    IsResume = _isResume
                };
            }
        }

        // Reset for next chain
        _currentFromStates = null;
        _currentTransition = default;
        _currentTargetState = default;
        _currentGuard = null;
        _currentAction = null;
        _isInterrupt = false;
        _isResume = false;
    }

    internal static string GetKey(S state, T transition) => $"{state}-{transition}";
}
