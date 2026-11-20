namespace Ananke.Abstractions.Distributed;

/// <summary>
/// Result of a coordinated action execution
/// </summary>
/// <typeparam name="R">Return type</typeparam>
public record CoordinatedActionResult<R>
{
    public required bool LockAcquired { get; init; }
    public required bool Success { get; init; }
    public R? Result { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }

    public static CoordinatedActionResult<R> LockFailed() => new()
    {
        LockAcquired = false,
        Success = false,
        ErrorMessage = "Failed to acquire distributed lock"
    };

    public static CoordinatedActionResult<R> Succeeded(R result) => new()
    {
        LockAcquired = true,
        Success = true,
        Result = result
    };

    public static CoordinatedActionResult<R> Failed(string message, Exception? exception = null) => new()
    {
        LockAcquired = true,
        Success = false,
        ErrorMessage = message,
        Exception = exception
    };
}

public interface IDistributedLock
{
    /// <summary>
    /// Executes an action (distributed system) that involves a state check/change.
    /// Tries the lock on <paramref name="resourceId"/> once, without waiting for another holder to release it:
    /// when another holder has it, returns <see cref="CoordinatedActionResult{R}.LockFailed"/> and the action
    /// does not run. An exception from the action, an <see cref="OperationCanceledException"/> included unless
    /// <paramref name="ct"/> is cancelled, is returned in <see cref="CoordinatedActionResult{R}.Failed"/>.
    /// </summary>
    /// <param name="resourceId">unique resource id - could be a correlation id or similar</param>
    /// <param name="action">a quick operation to read and update a state</param>
    /// <param name="ct">
    /// Cancels taking the lock: a cancelled token throws <see cref="OperationCanceledException"/>.
    /// Does not reach into <paramref name="action"/> itself — once the action starts, it runs to completion.
    /// </param>
    /// <returns>Result indicating if lock was acquired and action succeeded</returns>
    Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(string resourceId, Func<Task<R>> action, CancellationToken ct = default);
}
