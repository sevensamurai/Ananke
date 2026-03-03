namespace Ananke.Orchestration.Middleware;

/// <summary>
/// Cross-cutting behaviour applied around every job execution (logging, metrics, feature flags, etc.).
/// Register middlewares via <see cref="Extensions.OrchestrationOptions"/> or pass them directly
/// to <see cref="Execution.WorkflowRunner"/>. Middlewares are invoked in registration order.
/// </summary>
public interface IJobMiddleware<TState>
{
    /// <summary>
    /// Invokes the middleware. Call <paramref name="next"/> to continue down the pipeline.
    /// Return a different state value to replace the result seen by subsequent middlewares and the runner.
    /// </summary>
    Task<TState> InvokeAsync(
        string jobName,
        TState state,
        Func<Task<TState>> next,
        CancellationToken ct = default);
}
