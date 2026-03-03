namespace Ananke.Orchestration.Jobs;

/// <summary>
/// Controls when an interrupt pauses execution relative to the associated job.
/// </summary>
public enum InterruptMode
{
    /// <summary>Pause execution <em>before</em> the job runs. Resume starts at this job.</summary>
    Before,

    /// <summary>Pause execution <em>after</em> the job completes. Resume starts at the next job.</summary>
    After
}
