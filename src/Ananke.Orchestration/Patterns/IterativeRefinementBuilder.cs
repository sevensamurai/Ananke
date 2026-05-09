using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Routing;

namespace Ananke.Orchestration.Patterns;

/// <summary>
/// Fluent builder for the <b>Iterative Refinement</b> agentic pattern.
/// <para>
/// The pattern wires a single agent job in a self-loop. The agent refines its
/// output on each cycle, and execution continues until the <c>Until</c> predicate
/// is satisfied or the iteration cap (<see cref="MaxIterations"/>) is reached.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Generated workflow topology:</b></para>
/// <code>
/// refine → [condition met?] → __end__
///   ↑           │ no
///   └───────────┘
/// </code>
/// <para>
/// Simpler than <see cref="ReviewCritiqueBuilder{TState}"/>: one agent plays
/// both the generator and evaluator roles. Use this when the same agent can
/// both produce and assess its own output.
/// </para>
/// <para>
/// The returned <see cref="Workflow{TState}"/> is open for further customization
/// (checkpointing, tracing, metadata, extra jobs) before calling
/// <see cref="Workflow{TState}.RunAsync"/>.
/// </para>
/// <para>
/// Create instances via <see cref="AgenticPattern.IterativeRefinement{TState}"/>.
/// </para>
/// </remarks>
/// <typeparam name="TState">The workflow state type.</typeparam>
public sealed class IterativeRefinementBuilder<TState>
{
    private readonly string _name;
    private IJob<TState>? _agent;
    private Func<TState, bool>? _until;
    private int _maxIterations = 10;
    private Action<TState, LoopExitReason>? _onLoopExit;

    internal IterativeRefinementBuilder(string name) => _name = name;

    /// <summary>
    /// Sets the refinement agent — the job that produces and improves output
    /// on each iteration.
    /// </summary>
    /// <param name="agent">
    /// Any <see cref="IJob{TState}"/>: an <c>AgentJob</c>, a delegate job, or a
    /// custom implementation. This job executes on each loop cycle.
    /// </param>
    public IterativeRefinementBuilder<TState> WithAgent(IJob<TState> agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
        return this;
    }

    /// <summary>
    /// Sets the refinement agent as an inline delegate.
    /// </summary>
    public IterativeRefinementBuilder<TState> WithAgent(
        string name, Func<TState, CancellationToken, Task<TState>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);
        _agent = new DelegateJob<TState>(name, execute);
        return this;
    }

    /// <summary>
    /// Sets the termination predicate. The loop exits when this returns <c>true</c>
    /// after the agent completes, or when <see cref="MaxIterations"/> is reached.
    /// </summary>
    /// <param name="predicate">
    /// Evaluated against the state after the agent job. Return <c>true</c> to
    /// exit the loop (quality threshold met). Return <c>false</c> to refine again.
    /// </param>
    public IterativeRefinementBuilder<TState> Until(Func<TState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _until = predicate;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of refinement iterations. Default is 10.
    /// When reached without the <c>Until</c> predicate returning <c>true</c>,
    /// the loop exits with the current state.
    /// </summary>
    public IterativeRefinementBuilder<TState> MaxIterations(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        _maxIterations = max;
        return this;
    }

    /// <summary>
    /// Optional callback invoked when the loop terminates, with the final state
    /// and the reason for exit. Useful for logging or metrics.
    /// </summary>
    public IterativeRefinementBuilder<TState> OnLoopExit(Action<TState, LoopExitReason> handler)
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
    /// A required part is missing (agent or until predicate).
    /// </exception>
    public Workflow<TState> Build()
    {
        if (_agent is null)
            throw new InvalidOperationException(
                $"IterativeRefinement '{_name}': agent is required. Call WithAgent().");

        if (_until is null)
            throw new InvalidOperationException(
                $"IterativeRefinement '{_name}': termination predicate is required. Call Until().");

        var until = _until;
        var onLoopExit = _onLoopExit;

        if (onLoopExit is not null)
        {
            var maxIter = _maxIterations;
            var counter = 0;
            Func<TState, bool> wrappedUntil = state =>
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
                    return true;
                }
                return false;
            };

            return new Workflow<TState>(_name)
                .Job(_agent.Name, _agent)
                .Loop(_agent.Name, loopTarget: _agent.Name, exitTarget: Workflow.End,
                      until: wrappedUntil, maxIterations: maxIter + 1);
        }

        return new Workflow<TState>(_name)
            .Job(_agent.Name, _agent)
            .Loop(_agent.Name, loopTarget: _agent.Name, exitTarget: Workflow.End,
                  until: until, maxIterations: _maxIterations);
    }
}
