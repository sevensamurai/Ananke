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

/// <summary>
/// Internal marker interface used by the workflow builder's <c>Build()</c> method to detect whether
/// a job is backed by a profile-aware model router that can supply per-call cost rates.
/// This enables the budget build-time validation check (Phase 4.2).
/// </summary>
internal interface IProfileAwareJob
{
    bool HasProfileAwareModel { get; }
}
