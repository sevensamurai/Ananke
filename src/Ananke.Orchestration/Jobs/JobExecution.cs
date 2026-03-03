using System.Diagnostics;

namespace Ananke.Orchestration.Jobs;

public record JobExecution
{
    public required string JobName { get; init; }
    public required TimeSpan Duration { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }

    internal static JobExecution FromStopwatch(string jobName, Stopwatch sw, bool success, string? error = null) => new()
    {
        JobName = jobName,
        Duration = sw.Elapsed,
        Success = success,
        Error = error,
        StartedAt = DateTimeOffset.UtcNow - sw.Elapsed,
        CompletedAt = DateTimeOffset.UtcNow
    };
}
