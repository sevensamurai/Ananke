namespace Ananke.Orchestration.Jobs;

/// <summary>
/// Represents a single unit of work within a workflow.
/// Implement this interface to define reusable, named jobs.
/// </summary>
/// <typeparam name="TState">The immutable or value-type workflow state passed between jobs.</typeparam>
public interface IJob<TState>
{
    /// <summary>Gets the display name of the job, used in traces, logs, and history.</summary>
    string Name { get; }

    /// <summary>Executes the job against the current state and returns the updated state.</summary>
    Task<TState> ExecuteAsync(TState state, CancellationToken ct = default);
}
