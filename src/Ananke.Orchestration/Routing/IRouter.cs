using Ananke.Orchestration.Workflows;

namespace Ananke.Orchestration.Routing;

/// <summary>
/// Selects the next job to execute based on the current workflow state.
/// Use <see cref="Workflow.Decide{TState}"/> or <see cref="Workflow.DecideAsync{TState}"/>
/// to create an inline router without implementing this interface directly.
/// </summary>
/// <remarks>
/// Implement <see cref="RouteAsync(TState, CancellationToken)"/> for the full contract.
/// The single-parameter overload forwards to it with <see cref="CancellationToken.None"/>.
/// </remarks>
public interface IRouter<TState>
{
    /// <summary>
    /// Returns the name of the next job, or <see cref="Workflow.End"/> to terminate the workflow.
    /// Forwards to <see cref="RouteAsync(TState, CancellationToken)"/> with <see cref="CancellationToken.None"/>.
    /// </summary>
    Task<string> RouteAsync(TState state) => RouteAsync(state, CancellationToken.None);

    /// <summary>
    /// Returns the name of the next job, or <see cref="Workflow.End"/> to terminate the workflow.
    /// Implementations that perform async I/O (e.g. LLM calls) should observe <paramref name="ct"/>.
    /// </summary>
    Task<string> RouteAsync(TState state, CancellationToken ct);
}
