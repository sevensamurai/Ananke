using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Routing;

namespace Ananke.Orchestration.Patterns;

/// <summary>
/// Fluent builder for the <b>Review and Critique</b> agentic pattern.
/// <para>
/// The pattern wires two jobs — a <em>generator</em> and a <em>critic</em> — in a
/// feedback loop. The generator produces or revises output, the critic evaluates it,
/// and execution cycles until the <c>Until</c> predicate is satisfied or the
/// iteration cap (<see cref="MaxIterations"/>) is reached.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Generated workflow topology:</b></para>
/// <code>
/// generator → critic → [condition met?] → __end__
///                ↑          │ no
///                └──────────┘
/// </code>
/// <para>
/// The returned <see cref="Workflow{TState}"/> is open for further customization
/// (checkpointing, tracing, metadata, extra jobs) before calling
/// <see cref="Workflow{TState}.RunAsync"/>.
/// </para>
/// <para>
/// Create instances via <see cref="AgenticPattern.ReviewCritique{TState}"/>.
/// </para>
/// </remarks>
/// <typeparam name="TState">The workflow state type.</typeparam>
public sealed class ReviewCritiqueBuilder<TState>
{
    private readonly string _name;
    private IJob<TState>? _generator;
    private IJob<TState>? _critic;
    private Func<TState, bool>? _until;
    private int _maxIterations = 5;
    private Action<TState, LoopExitReason>? _onLoopExit;

    internal ReviewCritiqueBuilder(string name) => _name = name;

    /// <summary>
    /// Sets the generator job — the agent that produces or revises the output
    /// on each iteration.
    /// </summary>
    /// <param name="generator">
    /// Any <see cref="IJob{TState}"/>: an <c>AgentJob</c>, a delegate job, or a
    /// custom implementation. This job is the first to execute on each loop cycle.
    /// </param>
    public ReviewCritiqueBuilder<TState> WithGenerator(IJob<TState> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
        return this;
    }

    /// <summary>
    /// Sets the generator as an inline delegate.
    /// </summary>
    public ReviewCritiqueBuilder<TState> WithGenerator(
        string name, Func<TState, CancellationToken, Task<TState>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);
        _generator = new DelegateJob<TState>(name, execute);
        return this;
    }

    /// <summary>
    /// Sets the critic job — the agent that evaluates the generator's output and
    /// updates the state with quality scores, feedback, or approval signals.
    /// </summary>
    /// <param name="critic">
    /// Any <see cref="IJob{TState}"/>. The critic's output state is what the
    /// <c>Until</c> predicate evaluates.
    /// </param>
    public ReviewCritiqueBuilder<TState> WithCritic(IJob<TState> critic)
    {
        ArgumentNullException.ThrowIfNull(critic);
        _critic = critic;
        return this;
    }

    /// <summary>
    /// Sets the critic as an inline delegate.
    /// </summary>
    public ReviewCritiqueBuilder<TState> WithCritic(
        string name, Func<TState, CancellationToken, Task<TState>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);
        _critic = new DelegateJob<TState>(name, execute);
        return this;
    }

    /// <summary>
    /// Sets the termination predicate. The loop exits when this returns <c>true</c>
    /// after the critic completes, or when <see cref="MaxIterations"/> is reached.
    /// </summary>
    /// <param name="predicate">
    /// Evaluated against the state after the critic job. Return <c>true</c> to
    /// exit the loop (quality threshold met). Return <c>false</c> to loop back
    /// to the generator.
    /// </param>
    public ReviewCritiqueBuilder<TState> Until(Func<TState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _until = predicate;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of generate-critique iterations. Default is 5.
    /// When reached without the <c>Until</c> predicate returning <c>true</c>,
    /// the loop exits with the current state.
    /// </summary>
    public ReviewCritiqueBuilder<TState> MaxIterations(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        _maxIterations = max;
        return this;
    }

    /// <summary>
    /// Optional callback invoked when the loop terminates, with the final state
    /// and the reason for exit. Useful for logging or metrics.
    /// </summary>
    public ReviewCritiqueBuilder<TState> OnLoopExit(Action<TState, LoopExitReason> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _onLoopExit = handler;
        return this;
    }

    /// <summary>
    /// Validates the configuration and builds the <see cref="Workflow{TState}"/>.
    /// The returned workflow can be further customized with checkpointing, tracing,
    /// metadata, or embedded as a sub-workflow.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A required part is missing (generator, critic, or until predicate).
    /// </exception>
    public Workflow<TState> Build()
    {
        if (_generator is null)
            throw new InvalidOperationException(
                $"ReviewCritique '{_name}': generator is required. Call WithGenerator().");

        if (_critic is null)
            throw new InvalidOperationException(
                $"ReviewCritique '{_name}': critic is required. Call WithCritic().");

        if (_until is null)
            throw new InvalidOperationException(
                $"ReviewCritique '{_name}': termination predicate is required. Call Until().");

        var until = _until;
        var onLoopExit = _onLoopExit;

        // Wrap the Until predicate to invoke the OnLoopExit callback when the
        // condition is met. MaxIterationsReached is handled by the runner —
        // the callback fires via a wrapper job that observes the loop counter.
        Func<TState, bool> wrappedUntil;
        if (onLoopExit is not null)
        {
            var maxIter = _maxIterations;
            var counter = 0;
            wrappedUntil = state =>
            {
                counter++;
                if (until(state))
                {
                    onLoopExit(state, LoopExitReason.ConditionMet);
                    return true;
                }
                if (counter >= maxIter)
                {
                    onLoopExit(state, LoopExitReason.MaxIterationsReached);
                    // Return true to tell the loop to exit; the reason is already reported.
                    return true;
                }
                return false;
            };
            // Use maxIter + 1 so the runner's cap is never the one that triggers —
            // our wrapper handles both exits via the callback.
            return new Workflow<TState>(_name)
                .Job(_generator.Name, _generator)
                .Job(_critic.Name, _critic)
                .Then(_generator.Name, _critic.Name)
                .Loop(_critic.Name, loopTarget: _generator.Name, exitTarget: Workflow.End,
                      until: wrappedUntil, maxIterations: maxIter + 1);
        }

        return new Workflow<TState>(_name)
            .Job(_generator.Name, _generator)
            .Job(_critic.Name, _critic)
            .Then(_generator.Name, _critic.Name)
            .Loop(_critic.Name, loopTarget: _generator.Name, exitTarget: Workflow.End,
                  until: until, maxIterations: _maxIterations);
    }
}
