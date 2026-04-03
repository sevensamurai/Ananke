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
    /// Executes an action (distributed system) that involves a state check/change
    /// </summary>
    /// <param name="resourceId">unique resource id - could be a correlation id or similar</param>
    /// <param name="action">a quick operation to read and update a state</param>
    /// <returns>Result indicating if lock was acquired and action succeeded</returns>
    Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(string resourceId, Func<Task<R>> action);

    /// <summary>
    /// Executes an action with retry on lock acquisition failure
    /// </summary>
    /// <param name="resourceId">unique resource id</param>
    /// <param name="action">operation to execute</param>
    /// <param name="maxRetries">maximum retry attempts</param>
    /// <param name="retryDelayMs">delay between retries in milliseconds</param>
    /// <returns>Result indicating if lock was acquired and action succeeded</returns>
    Task<CoordinatedActionResult<R>> RunCoordinatedActionWithRetryAsync<R>(
        string resourceId,
        Func<Task<R>> action,
        int maxRetries = 3,
        int retryDelayMs = 100);
}
