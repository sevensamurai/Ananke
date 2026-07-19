using System.Diagnostics;

namespace Ananke.Orchestration.Jobs;

/// <summary>
/// Structured outcome code for a job execution. Provides richer signal than
/// the boolean <see cref="JobExecution.Success"/> flag — distinguishes
/// between different kinds of success and failure.
/// </summary>
public enum JobOutcome
{
    /// <summary>Job completed successfully and produced a useful result.</summary>
    Success,

    /// <summary>Job failed with an error (exception, timeout, etc.).</summary>
    Failed,

    /// <summary>
    /// Job completed without error but the agent could not meaningfully
    /// serve the request — it deflected, said "I don't know", or lacked
    /// the tools/domain knowledge to help. The workflow succeeded technically
    /// but the result has no value to the caller.
    /// </summary>
    /// <remarks>
    /// Set by the agent job when it detects a deflection in the LLM's
    /// response. Used by <c>FailureClassifier</c> for deterministic
    /// capability mismatch detection — no heuristic pattern matching needed.
    /// </remarks>
    Deflected
}

public record JobExecution
{
    public required string JobName { get; init; }
    public required TimeSpan Duration { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Structured outcome code providing richer signal than <see cref="Success"/>.
    /// Defaults to <see cref="JobOutcome.Success"/> when <see cref="Success"/> is
    /// <see langword="true"/>, and <see cref="JobOutcome.Failed"/> otherwise.
    /// Agent jobs may set <see cref="JobOutcome.Deflected"/> when the LLM
    /// cannot serve the request.
    /// </summary>
    public JobOutcome Outcome { get; init; }

    internal static JobExecution FromStopwatch(
        string jobName, Stopwatch sw, bool success,
        string? error = null, JobOutcome? outcome = null) => new()
        {
            JobName = jobName,
            Duration = sw.Elapsed,
            Success = success,
            Error = error,
            StartedAt = DateTimeOffset.UtcNow - sw.Elapsed,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = outcome ?? (success ? JobOutcome.Success : JobOutcome.Failed)
        };
}
