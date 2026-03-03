namespace Ananke.Orchestration.Routing;

/// <summary>
/// Selects the next job to execute based on the current workflow state.
/// Use <see cref="Workflow.Decide{TState}"/> or <see cref="Workflow.DecideAsync{TState}"/>
/// to create an inline router without implementing this interface directly.
/// </summary>
public interface IRouter<TState>
{
    /// <summary>
    /// Returns the name of the next job, or <see cref="Workflow.End"/> to terminate the workflow.
    /// </summary>
    Task<string> RouteAsync(TState state);

    /// <summary>
    /// Returns the name of the next job, or <see cref="Workflow.End"/> to terminate the workflow.
    /// Implementations that perform async I/O (e.g. LLM calls) should observe <paramref name="ct"/>.
    /// </summary>
    Task<string> RouteAsync(TState state, CancellationToken ct) => RouteAsync(state);
}
